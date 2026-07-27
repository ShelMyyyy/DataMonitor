using System.Net.Sockets;
using DataMonitor.Core.Models;
using DataMonitor.Core.Models.Protocol;
using DataMonitor.Core.Protocols;

namespace DataMonitor.Core.Services;

/// <summary>
/// 设备发现服务
/// 通过并行TCP端口扫描发现网络中的下位机设备。
/// 
/// 扫描流程：
/// 1. 对端口 8888/8889/8890 并行发起TCP连接
/// 2. 每个端口使用对应的协议帧头发送 InfoRequest 帧
/// 3. 读取 InfoResponse 帧获取设备类型、固件版本等信息
/// 4. 断开探测连接，返回发现的设备信息列表
/// 
/// 端口与协议的映射关系：
/// - 8888 → DefaultProtocol（帧头 0xAA 0x55）
/// - 8889 → TemperatureProtocol（帧头 0xBB 0x66）
/// - 8890 → PressureProtocol（帧头 0xCC 0x77）
/// </summary>
public static class DeviceDiscoverer
{
    /// <summary>端口号到协议编解码器的映射</summary>
    private static readonly Dictionary<int, IProtocolCodec> PortProtocols = new()
    {
        { 8888, new DefaultProtocol() },
        { 8889, new TemperatureProtocol() },
        { 8890, new PressureProtocol() }
    };

    /// <summary>扫描的端口列表</summary>
    private static readonly int[] ScanPorts = [8888, 8889, 8890];

    /// <summary>单个端口的连接和读取超时时间（毫秒）</summary>
    private const int TimeoutMs = 2000;

    /// <summary>
    /// 并行扫描所有默认端口，返回发现的设备列表
    /// </summary>
    public static async Task<List<DeviceInfo>> ScanAsync()
    {
        // 并行扫描提高效率：3个端口同时探测
        var tasks = ScanPorts.Select(p => ScanPortAsync("127.0.0.1", p));
        var results = await Task.WhenAll(tasks);
        return results.Where(r => r != null).Select(r => r!).ToList();
    }

    /// <summary>
    /// 扫描单个IP:端口
    /// </summary>
    /// <returns>发现的设备信息，或null（端口无响应）</returns>
    private static async Task<DeviceInfo?> ScanPortAsync(string ip, int port)
    {
        try
        {
            // 获取该端口对应的协议（未知端口回退到DefaultProtocol）
            if (!PortProtocols.TryGetValue(port, out var proto))
                proto = new DefaultProtocol();

            // 1. TCP连接（带超时）
            using var tcp = new TcpClient();
            var connectTask = tcp.ConnectAsync(ip, port);
            if (await Task.WhenAny(connectTask, Task.Delay(TimeoutMs)) != connectTask)
                return null; // 连接超时
            await connectTask; // 传播异常

            // 2. 发送 InfoRequest（帧类型0x07，空Payload）
            using var ns = tcp.GetStream();
            await ns.WriteAsync(proto.BuildFrame(0x07, []));

            // 3. 接收并解析 InfoResponse
            var buf = new byte[512]; int total = 0;
            using var readCts = new CancellationTokenSource(TimeoutMs);
            try
            {
                while (total < buf.Length && !readCts.IsCancellationRequested)
                {
                    int n = await ns.ReadAsync(buf.AsMemory(total, buf.Length - total), readCts.Token);
                    if (n == 0) break;
                    total += n;

                    // 尝试解析缓冲区中的帧
                    var result = proto.TryDecode(buf, 0, total, out int consumed);
                    if (result != null && result.FrameType == FrameType.InfoResponse && result.DeviceInfo != null)
                    {
                        // 成功获取设备信息，填充IP和端口
                        result.DeviceInfo.IpAddress = ip;
                        result.DeviceInfo.Port = port;
                        return result.DeviceInfo;
                    }
                    // 跳过无效数据，继续读取
                    if (consumed > 0)
                    {
                        int rem = total - consumed;
                        if (rem > 0) Array.Copy(buf, consumed, buf, 0, rem);
                        total = rem;
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (IOException) { }

            return null;
        }
        catch
        {
            // 任何异常都视为未发现设备
            return null;
        }
    }
}
