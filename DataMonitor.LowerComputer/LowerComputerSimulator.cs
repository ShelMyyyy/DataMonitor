using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using DataMonitor.Core.Models;
using DataMonitor.Core.Models.Protocol;
using DataMonitor.Core.Protocols;
using DataMonitor.Core.Services;

namespace DataMonitor.LowerComputer;

/// <summary>
/// 下位机模拟器 — 使用可插拔协议编解码器。
/// 每种设备类型使用不同协议（不同帧头+校验算法）。
/// </summary>
public class LowerComputerSimulator
{
    private TcpListener? _listener;
    private readonly Dictionary<ParameterId, float> _parameters;
    private readonly Random _random = new();
    private CancellationTokenSource? _cts;
    private readonly DeviceType _deviceType;
    private readonly IProtocolCodec _proto;

    public LowerComputerSimulator(DeviceType type = DeviceType.GeneralSensor)
    {
        _deviceType = type;
        _proto = type switch
        {
            DeviceType.TemperatureMonitor => new TemperatureProtocol(),
            DeviceType.PressureController => new PressureProtocol(),
            _ => new DefaultProtocol()
        };
        _parameters = type switch
        {
            DeviceType.TemperatureMonitor => new() { {ParameterId.TargetTemperature,60},{ParameterId.AlarmThreshold,75},{ParameterId.SampleInterval,1500} },
            DeviceType.PressureController => new() { {ParameterId.MaxPressure,300},{ParameterId.FlowRateLimit,80},{ParameterId.SampleInterval,500} },
            _ => new() { {ParameterId.TargetTemperature,80},{ParameterId.MaxPressure,200},{ParameterId.FlowRateLimit,50},{ParameterId.SampleInterval,1000},{ParameterId.AlarmThreshold,95} }
        };
    }

    public async Task StartAsync(int port, CancellationToken ct = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _listener = new TcpListener(IPAddress.Any, port);
        _listener.Start();
        Console.WriteLine($"[{port}] 启动 {_proto.Name} | {new DeviceInfo{Type=_deviceType}.Name}");
        PrintParams();
        try
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                var c = await _listener.AcceptTcpClientAsync(_cts.Token);
                Console.WriteLine($"[{port}] 客户端连接:{c.Client.RemoteEndPoint}");
                _ = HandleClient(c, _cts.Token);
            }
        }
        catch (OperationCanceledException) { }
        finally { _listener?.Stop(); Console.WriteLine($"[{port}] 已停止"); }
    }

    private async Task HandleClient(TcpClient client, CancellationToken ct)
    {
        var buf = new byte[4096]; int cnt = 0;
        try
        {
            using (client)
            using (var ns = client.GetStream())
            {
                using var dc = CancellationTokenSource.CreateLinkedTokenSource(ct);
                var dt = SendLoop(ns, dc.Token);
                while (!ct.IsCancellationRequested && client.Connected)
                {
                    int n = await ns.ReadAsync(buf.AsMemory(cnt, buf.Length - cnt), ct);
                    if (n == 0) break; cnt += n;
                    int o = 0, c = cnt;
                    while (c >= 8)
                    {
                        var r = _proto.TryDecode(buf, o, c, out int u);
                        if (r != null) { o += u; c -= u; await ProcessCmd(ns, r, ct); }
                        else if (u > 0) { o += u; c -= u; }
                        else break;
                    }
                    if (c > 0 && o > 0) Array.Copy(buf, o, buf, 0, c);
                    cnt = c;
                }
                dc.Cancel(); try { await dt; } catch (OperationCanceledException) { }
            }
        }
        catch (OperationCanceledException) { } catch (IOException) { }
        finally { Console.WriteLine($"[{_deviceType}] 客户端断开"); }
    }

    private async Task SendLoop(NetworkStream ns, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                int iv = (int)_parameters.GetValueOrDefault(ParameterId.SampleInterval, 1000);
                await Task.Delay(Math.Max(iv, 100), ct);
                var data = GenData();
                await ns.WriteAsync(_proto.EncodeRealTimeData(data), ct);
            }
        }
        catch (OperationCanceledException) { } catch (IOException) { }
    }

    private TelemetryData GenData()
    {
        float bt = _parameters.GetValueOrDefault(ParameterId.TargetTemperature, 80f);
        float at = _parameters.GetValueOrDefault(ParameterId.AlarmThreshold, 95f);
        float mp = _parameters.GetValueOrDefault(ParameterId.MaxPressure, 200f);
        float fl = _parameters.GetValueOrDefault(ParameterId.FlowRateLimit, 50f);
        float t = bt + (float)(_random.NextDouble()*20-10);
        float h = 40f+(float)(_random.NextDouble()*40);
        float p = 80f+(float)(_random.NextDouble()*mp*0.5f);
        float f = (float)(_random.NextDouble()*fl);
        byte s = 0; if (t>at) s=1; if (t>at*1.2f) s=2;
        return new TelemetryData { Timestamp=DateTime.Now, Temperature=(float)Math.Round(t,1), Humidity=(float)Math.Round(h,1), Pressure=(float)Math.Round(p,1), FlowRate=(float)Math.Round(f,2), Status=s };
    }

    private async Task ProcessCmd(NetworkStream ns, DecodeResult cmd, CancellationToken ct)
    {
        if (cmd.FrameType == FrameType.ReadParamRequest && cmd.ParameterId.HasValue)
        {
            float v = _parameters.GetValueOrDefault(cmd.ParameterId.Value);
            byte[] pl = new byte[5]; pl[0]=(byte)cmd.ParameterId.Value;
            BinaryPrimitives.WriteSingleLittleEndian(pl.AsSpan(1), v);
            await ns.WriteAsync(_proto.BuildFrame(0x03, pl), ct);
        }
        else if (cmd.FrameType == FrameType.WriteParamRequest && cmd.ParameterId.HasValue && cmd.ParameterValue.HasValue)
        {
            bool ok = _parameters.ContainsKey(cmd.ParameterId.Value);
            if (ok) _parameters[cmd.ParameterId.Value]=cmd.ParameterValue.Value;
            byte[] pl = new byte[2]; pl[0]=(byte)cmd.ParameterId.Value; pl[1]=ok?(byte)0:(byte)1;
            await ns.WriteAsync(_proto.BuildFrame(0x05, pl), ct);
            if (ok) PrintParams();
        }
        else if (cmd.FrameType == FrameType.InfoRequest)
        {
            byte[] pl = new byte[4];
            pl[0]=(byte)_deviceType; pl[1]=1; pl[2]=0; pl[3]=(byte)_parameters.Count;
            await ns.WriteAsync(_proto.BuildFrame(0x08, pl), ct);
        }
    }

    private void PrintParams()
    {
        foreach (var kv in _parameters)
            Console.WriteLine($"  {kv.Key}={kv.Value:F2}");
    }
}

/// <summary>
/// 程序入口 — 支持多实例并行启动。
/// 用法: dotnet run -- [port1:type1] [port2:type2] ...
/// 示例: dotnet run -- 8888 8889:temp 8890:pressure
/// 无参数默认启动 8888 (通用传感器)
/// </summary>
public class Program
{
    public static async Task Main(string[] args)
    {
        var specs = new List<(int port, DeviceType type)>();
        if (args.Length == 0)
            specs.Add((8888, DeviceType.GeneralSensor));
        else
            foreach (var a in args)
            {
                var parts = a.Split(':');
                int port = int.TryParse(parts[0], out int p) ? p : 8888;
                DeviceType type = DeviceType.GeneralSensor;
                if (parts.Length > 1)
                    type = parts[1].ToLower() switch { "temp"=>DeviceType.TemperatureMonitor, "pressure"=>DeviceType.PressureController, _=>DeviceType.GeneralSensor };
                specs.Add((port, type));
            }

        Console.WriteLine("=========================================");
        Console.WriteLine("       下位机模拟器 - 多实例启动");
        Console.WriteLine("=========================================");
        foreach (var (p, t) in specs) Console.WriteLine($"  {p} -> {new DeviceInfo{Type=t}.Name}");
        Console.WriteLine("  Ctrl+C 停止全部");
        Console.WriteLine("=========================================");

        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); Console.WriteLine("\n停止中..."); };
        var tasks = specs.Select(s => new LowerComputerSimulator(s.type).StartAsync(s.port, cts.Token));
        try { await Task.WhenAll(tasks); } catch (Exception ex) { Console.WriteLine($"错误: {ex.Message}"); }
        Console.WriteLine("按任意键退出..."); Console.ReadKey();
    }
}
