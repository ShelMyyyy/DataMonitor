using System.Buffers.Binary;
using System.Net.Sockets;
using DataMonitor.Core.Interfaces;
using DataMonitor.Core.Models;
using DataMonitor.Core.Models.Protocol;
using DataMonitor.Core.Services;

namespace DataMonitor.Plugins.Default;

/// <summary>
/// 默认设备插件
/// 基于TCP + 自定义二进制协议实现 IDevicePlugin 接口。
/// 
/// 核心设计：
/// - 使用 TcpClient 建立到指定IP:端口的连接
/// - 启动后台接收循环，持续从 NetworkStream 读取数据
/// - 使用滑动窗口缓冲区 + ProtocolDecoder 解析帧
/// - 通过 TaskCompletionSource 实现请求-响应模式（读/写参数）
/// - 通过事件向外部推送遥测数据
/// 
/// 线程安全：
/// - 所有TCP写入操作必须通过该类的异步方法
/// - _pendingResponse 在同一时刻只能被一个读/写操作占用
///   （MVVM层串行调用保证了这一点）
/// </summary>
public class DefaultDevicePlugin : IDevicePlugin
{
    // ===== TCP通讯资源 =====

    /// <summary>TCP客户端，null表示未连接</summary>
    private TcpClient? _tcpClient;

    /// <summary>网络流，从 TcpClient.GetStream() 获取</summary>
    private NetworkStream? _stream;

    /// <summary>取消令牌源，用于停止接收循环和中断所有异步操作</summary>
    private CancellationTokenSource? _cts;

    // ===== 接收缓冲 =====

    /// <summary>
    /// 接收缓冲区（固定大小4096字节）
    /// 使用环形缓冲策略：数据写入 _bufferOffset+_bufferCount 位置，
    /// 解析后未消费数据前移到 _bufferOffset 处
    /// </summary>
    private readonly byte[] _receiveBuffer = new byte[4096];

    /// <summary>缓冲区有效数据的起始偏移（通常为0）</summary>
    private int _bufferOffset;

    /// <summary>缓冲区中有效数据的字节数</summary>
    private int _bufferCount;

    // ===== 请求-响应匹配 =====

    /// <summary>
    /// 待处理的参数读写请求
    /// 当发送读/写参数请求后，设置为一个 TaskCompletionSource；
    /// 接收循环收到对应的响应帧时通过 TrySetResult 唤醒等待方。
    /// null 表示当前无等待中的请求。
    /// </summary>
    private TaskCompletionSource<DecodeResult>? _pendingResponse;

    // ===== 公开属性 =====

    /// <summary>插件标识名</summary>
    public string PluginName => "DefaultDevice";

    /// <summary>是否已连接到下位机</summary>
    public bool IsConnected => _tcpClient?.Connected ?? false;

    // ===== 公开事件 =====

    /// <summary>遥测数据到达事件（在后台线程触发）</summary>
    public event EventHandler<TelemetryData>? TelemetryDataReceived;

    /// <summary>连接状态变化事件</summary>
    public event EventHandler<bool>? ConnectionStateChanged;

    /// <summary>通讯日志事件</summary>
    public event EventHandler<string>? LogMessage;

    // ===== 连接管理 =====

    /// <summary>
    /// 建立与下位机的TCP连接并启动数据接收循环
    /// </summary>
    /// <param name="ipAddress">目标IP地址</param>
    /// <param name="port">目标TCP端口</param>
    /// <param name="cancellationToken">外部取消令牌</param>
    public async Task ConnectAsync(string ipAddress, int port, CancellationToken cancellationToken = default)
    {
        // 先断开已有连接（如果有的话），确保干净的状态
        await DisconnectAsync();

        // 创建新的取消令牌源，用于控制整个连接生命周期
        _cts = new CancellationTokenSource();

        // 建立TCP连接
        _tcpClient = new TcpClient();
        await _tcpClient.ConnectAsync(ipAddress, port, cancellationToken);

        // 获取网络流
        _stream = _tcpClient.GetStream();

        // 重置缓冲区状态
        _bufferOffset = 0;
        _bufferCount = 0;

        // 通知外部连接成功
        LogMessage?.Invoke(this, $"已连接到设备 {ipAddress}:{port}");
        ConnectionStateChanged?.Invoke(this, true);

        // 启动后台接收循环（fire-and-forget，不等待）
        // 使用 Task.Run 将接收循环调度到线程池，避免阻塞当前调用
        _ = Task.Run(() => ReceiveLoop(_cts.Token), _cts.Token);
    }

    /// <summary>
    /// 断开TCP连接，停止接收循环，释放所有网络资源
    /// </summary>
    public async Task DisconnectAsync()
    {
        // 取消令牌会导致 ReceiveLoop 中的操作抛出 OperationCanceledException
        _cts?.Cancel();

        // 关闭TCP连接（会中断 NetworkStream 的阻塞读取）
        _tcpClient?.Close();
        _stream?.Dispose();

        // 释放引用
        _tcpClient = null;
        _stream = null;

        // 释放取消令牌源
        _cts?.Dispose();
        _cts = null;

        // 重置缓冲区
        _bufferOffset = 0;
        _bufferCount = 0;

        // 通知外部连接断开
        LogMessage?.Invoke(this, "已断开连接");
        ConnectionStateChanged?.Invoke(this, false);

        await Task.CompletedTask;
    }

    // ===== 参数读写 =====

    /// <summary>
    /// 读取指定设备参数的值
    /// 发送 ReadParamRequest → 等待 ReadParamResponse → 返回参数值
    /// 超时时间：5秒
    /// </summary>
    public async Task<float> ReadParameterAsync(ParameterId paramId, CancellationToken cancellationToken = default)
    {
        if (!IsConnected)
            throw new InvalidOperationException("设备未连接");

        // 使用 ProtocolEncoder 构造读参数请求帧
        byte[] frame = ProtocolEncoder.EncodeReadParamRequest(paramId);
        LogMessage?.Invoke(this, $"发送读参数请求: {(byte)paramId:X2}");

        // 创建一个合并取消令牌：外部传入的 + 当前连接的
        // 这样无论是外部取消还是连接断开，都能中断等待
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _cts!.Token);

        // 创建TaskCompletionSource，等待接收循环匹配对应的响应帧
        var tcs = new TaskCompletionSource<DecodeResult>();
        _pendingResponse = tcs;

        // 发送请求帧到网络流
        await _stream!.WriteAsync(frame, linkedCts.Token);

        try
        {
            // 设置5秒超时
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var finalCts = CancellationTokenSource.CreateLinkedTokenSource(
                timeoutCts.Token, linkedCts.Token);

            // 超时触发时自动取消 TCS
            await using (finalCts.Token.Register(() => tcs.TrySetCanceled()))
            {
                // 等待接收循环设置结果
                var result = await tcs.Task;

                // 验证响应帧类型和数据的完整性
                if (result.FrameType == FrameType.ReadParamResponse &&
                    result.ParameterValue.HasValue)
                {
                    LogMessage?.Invoke(this,
                        $"收到读参数响应: {(byte)paramId:X2} = {result.ParameterValue.Value:F2}");
                    return result.ParameterValue.Value;
                }
            }
        }
        catch (OperationCanceledException)
        {
            LogMessage?.Invoke(this, $"读参数超时: {(byte)paramId:X2}");
            throw new TimeoutException($"读参数超时: {paramId}");
        }
        finally
        {
            // 无论成功或失败，都要清除等待状态
            _pendingResponse = null;
        }

        throw new InvalidOperationException($"读参数失败: {paramId}");
    }

    /// <summary>
    /// 修改指定设备参数
    /// 发送 WriteParamRequest → 等待 WriteParamResponse → 返回操作结果
    /// 超时时间：5秒
    /// </summary>
    public async Task<bool> WriteParameterAsync(ParameterId paramId, float value, CancellationToken cancellationToken = default)
    {
        if (!IsConnected)
            throw new InvalidOperationException("设备未连接");

        // 使用 ProtocolEncoder 构造写参数请求帧
        byte[] frame = ProtocolEncoder.EncodeWriteParamRequest(paramId, value);
        LogMessage?.Invoke(this, $"发送写参数请求: {(byte)paramId:X2} = {value:F2}");

        // 合并取消令牌
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _cts!.Token);

        // 创建等待器
        var tcs = new TaskCompletionSource<DecodeResult>();
        _pendingResponse = tcs;

        // 发送请求
        await _stream!.WriteAsync(frame, linkedCts.Token);

        try
        {
            // 5秒超时
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var finalCts = CancellationTokenSource.CreateLinkedTokenSource(
                timeoutCts.Token, linkedCts.Token);

            await using (finalCts.Token.Register(() => tcs.TrySetCanceled()))
            {
                var result = await tcs.Task;

                if (result.FrameType == FrameType.WriteParamResponse &&
                    result.Success.HasValue)
                {
                    LogMessage?.Invoke(this,
                        $"收到写参数响应: {(byte)paramId:X2} => {(result.Success.Value ? "成功" : "失败")}");
                    return result.Success.Value;
                }
            }
        }
        catch (OperationCanceledException)
        {
            LogMessage?.Invoke(this, $"写参数超时: {(byte)paramId:X2}");
            throw new TimeoutException($"写参数超时: {paramId}");
        }
        finally
        {
            _pendingResponse = null;
        }

        return false;
    }

    // ===== 接收循环 =====

    /// <summary>
    /// 后台接收循环
    /// 在独立线程中持续读取TCP流数据，解析帧并分发处理。
    /// 
    /// 缓冲区策略：
    /// - 新数据追加到 _bufferOffset+_bufferCount 处
    /// - 解析后如有未消费数据，前移到 _bufferOffset 处
    /// - 避免频繁的数组复制，只在必要时移动
    /// </summary>
    private async Task ReceiveLoop(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && IsConnected)
            {
                // 从网络流读取数据到缓冲区
                // 写入位置：_bufferOffset + _bufferCount
                // 可写入长度：总长度 - 已占用
                int bytesRead = await _stream!.ReadAsync(
                    _receiveBuffer.AsMemory(
                        _bufferOffset + _bufferCount,
                        _receiveBuffer.Length - _bufferOffset - _bufferCount),
                    ct);

                // ReadAsync 返回0表示远端关闭了连接
                if (bytesRead == 0)
                {
                    LogMessage?.Invoke(this, "远端已关闭连接");
                    break;
                }

                _bufferCount += bytesRead;

                // ---- 循环解析缓冲区中的所有完整帧 ----
                int searchOffset = _bufferOffset;
                int searchCount = _bufferCount;

                while (searchCount >= ProtocolConstants.MinFrameLength)
                {
                    var result = ProtocolDecoder.TryDecode(
                        _receiveBuffer, searchOffset, searchCount, out int consumed);

                    if (result != null)
                    {
                        // 成功解析一帧，推进搜索位置
                        searchOffset += consumed;
                        searchCount -= consumed;

                        // 分发处理解析结果
                        ProcessDecodedFrame(result);
                    }
                    else if (consumed > 0)
                    {
                        // 解析失败但消费了数据（跳过了无效字节），继续搜索
                        searchOffset += consumed;
                        searchCount -= consumed;
                    }
                    else
                    {
                        // consumed==0：数据不足以构成完整帧，跳出等待更多数据
                        break;
                    }
                }

                // ---- 整理缓冲区：将未消费数据移到开头 ----
                // 这避免了缓冲区在长时间运行后出现大量未消费的前导数据
                if (searchCount > 0 && searchOffset > _bufferOffset)
                {
                    Array.Copy(_receiveBuffer, searchOffset,
                               _receiveBuffer, _bufferOffset, searchCount);
                }
                _bufferCount = searchCount;
            }
        }
        catch (OperationCanceledException)
        {
            // 预期的取消（断开连接时），不记录错误
        }
        catch (ObjectDisposedException)
        {
            // 资源已被释放（DisconnectAsync 中调用），正常情况
        }
        catch (Exception ex)
        {
            // 意外的网络异常
            LogMessage?.Invoke(this, $"接收异常: {ex.Message}");
        }
        finally
        {
            // 接收循环退出后，确保断开连接
            await DisconnectAsync();
        }
    }

    /// <summary>
    /// 处理解析完成的帧
    /// 根据帧类型分发到不同的事件或完成等待中的请求
    /// </summary>
    /// <param name="result">已解析的帧数据</param>
    private void ProcessDecodedFrame(DecodeResult result)
    {
        switch (result.FrameType)
        {
            case FrameType.RealTimeData:
                // 实时数据 → 触发事件推送给ViewModel
                TelemetryDataReceived?.Invoke(this, result.TelemetryData!);
                break;

            case FrameType.ReadParamResponse:
            case FrameType.WriteParamResponse:
                // 参数读/写响应 → 完成等待中的TaskCompletionSource
                // 使用 ?. 是安全的：如果 _pendingResponse 为 null（比如连接断开后
                // 清除掉了），响应帧会被忽略
                _pendingResponse?.TrySetResult(result);
                break;

            case FrameType.Heartbeat:
                // 心跳帧：当前不做特殊处理，可扩展为更新设备在线时间戳
                break;

            // ReadParamRequest 和 WriteParamRequest 是上位机→下位机方向，
            // 上位机不会收到这些帧，此处不做处理
        }
    }

    // ===== 资源释放 =====

    /// <summary>
    /// 释放所有非托管和托管资源
    /// 确保连接断开、流关闭、取消令牌释放
    /// </summary>
    public void Dispose()
    {
        _cts?.Cancel();
        _stream?.Dispose();
        _tcpClient?.Dispose();
        _cts?.Dispose();
    }
}
