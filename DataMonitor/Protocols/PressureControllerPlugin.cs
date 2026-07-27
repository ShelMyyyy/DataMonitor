using System.Net.Sockets;
using DataMonitor.Core.Interfaces;
using DataMonitor.Core.Models;
using DataMonitor.Core.Models.Protocol;
using DataMonitor.Core.Protocols;
using DataMonitor.Core.Services;

namespace DataMonitor.Protocols;

/// <summary>
/// 压力控制器插件 — 使用 PressureProtocol (0xCC 0x77, 取反XOR校验)
/// 端口8890
/// </summary>
public class PressureControllerPlugin : IDevicePlugin
{
    private readonly PressureProtocol _proto = new();
    private TcpClient? _tcp; private NetworkStream? _ns;
    private CancellationTokenSource? _cts;
    private readonly byte[] _buf = new byte[4096]; private int _cnt;
    private TaskCompletionSource<DecodeResult>? _pending;

    public string PluginName => "压力控制器";
    public bool IsConnected => _tcp?.Connected ?? false;
    public event EventHandler<TelemetryData>? TelemetryDataReceived;
    public event EventHandler<bool>? ConnectionStateChanged;
    public event EventHandler<string>? LogMessage;

    public async Task ConnectAsync(string ip, int port, CancellationToken ct = default)
    {
        await DisconnectAsync();
        _cts = new(); _tcp = new(); await _tcp.ConnectAsync(ip, port, ct); _ns = _tcp.GetStream(); _cnt = 0;
        Log("已连接(Press协议)"); Conn(true);
        _ = Task.Run(() => RecvLoop(_cts.Token), _cts.Token);
    }

    public async Task DisconnectAsync()
    { _cts?.Cancel(); _tcp?.Close(); _ns?.Dispose(); _tcp = null; _ns = null; _cts?.Dispose(); _cts = null; _cnt = 0; Log("已断开"); Conn(false); await Task.CompletedTask; }

    public async Task<float> ReadParameterAsync(ParameterId pid, CancellationToken ct = default)
    {
        if (!IsConnected) throw new InvalidOperationException();
        var tcs = new TaskCompletionSource<DecodeResult>(); _pending = tcs;
        await _ns!.WriteAsync(_proto.EncodeReadParamRequest((byte)pid), CancellationTokenSource.CreateLinkedTokenSource(ct, _cts!.Token).Token);
        try
        {
            using var to = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var fc = CancellationTokenSource.CreateLinkedTokenSource(to.Token, ct, _cts.Token);
            await using (fc.Token.Register(() => tcs.TrySetCanceled()))
            { var r = await tcs.Task; if (r.ParameterValue.HasValue) return r.ParameterValue.Value; }
        }
        catch (OperationCanceledException) { throw new TimeoutException(); }
        finally { _pending = null; }
        throw new InvalidOperationException();
    }

    public async Task<bool> WriteParameterAsync(ParameterId pid, float v, CancellationToken ct = default)
    {
        if (!IsConnected) throw new InvalidOperationException();
        var tcs = new TaskCompletionSource<DecodeResult>(); _pending = tcs;
        await _ns!.WriteAsync(_proto.EncodeWriteParamRequest((byte)pid, v), CancellationTokenSource.CreateLinkedTokenSource(ct, _cts!.Token).Token);
        try
        {
            using var to = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var fc = CancellationTokenSource.CreateLinkedTokenSource(to.Token, ct, _cts.Token);
            await using (fc.Token.Register(() => tcs.TrySetCanceled()))
            { var r = await tcs.Task; if (r.Success.HasValue) return r.Success.Value; }
        }
        catch (OperationCanceledException) { throw new TimeoutException(); }
        finally { _pending = null; }
        return false;
    }

    private async void RecvLoop(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && IsConnected)
            {
                int n = await _ns!.ReadAsync(_buf.AsMemory(_cnt, _buf.Length - _cnt), ct);
                if (n == 0) break; _cnt += n;
                int o = 0, c = _cnt;
                while (c >= 8) { var r = _proto.TryDecode(_buf, o, c, out int u); if (r != null) { o += u; c -= u; Proc(r); } else if (u > 0) { o += u; c -= u; } else break; }
                if (c > 0 && o > 0) Array.Copy(_buf, o, _buf, 0, c);
                _cnt = c;
            }
        }
        catch (OperationCanceledException) { } catch (ObjectDisposedException) { }
        catch (Exception ex) { Log("接收异常:" + ex.Message); }
        finally { await DisconnectAsync(); }
    }

    private void Proc(DecodeResult r)
    {
        switch (r.FrameType)
        {
            case FrameType.RealTimeData: TelemetryDataReceived?.Invoke(this, r.TelemetryData!); break;
            case FrameType.ReadParamResponse: case FrameType.WriteParamResponse: _pending?.TrySetResult(r); break;
        }
    }

    private void Log(string m) => LogMessage?.Invoke(this, $"[压力] {m}");
    private void Conn(bool c) => ConnectionStateChanged?.Invoke(this, c);
    public void Dispose() { _cts?.Cancel(); _ns?.Dispose(); _tcp?.Dispose(); _cts?.Dispose(); }
}
