using System.Net.Sockets;
using DataMonitor.Core.Interfaces;
using DataMonitor.Core.Models;
using DataMonitor.Core.Models.Protocol;
using DataMonitor.Core.Services;

namespace DataMonitor.Plugins.Pressure;

/// <summary>
/// 压力控制器插件
/// 连接压力控制设备（默认端口8890），专注于压力/流量参数的监控。
/// </summary>
public class PressureControllerPlugin : IDevicePlugin
{
    private TcpClient? _tcpClient;
    private NetworkStream? _stream;
    private CancellationTokenSource? _cts;
    private readonly byte[] _receiveBuffer = new byte[4096];
    private int _bufferCount;
    private TaskCompletionSource<DecodeResult>? _pendingResponse;

    public string PluginName => "压力控制器";
    public bool IsConnected => _tcpClient?.Connected ?? false;
    public event EventHandler<TelemetryData>? TelemetryDataReceived;
    public event EventHandler<bool>? ConnectionStateChanged;
    public event EventHandler<string>? LogMessage;

    public async Task ConnectAsync(string ipAddress, int port, CancellationToken ct = default)
    {
        await DisconnectAsync();
        _cts = new CancellationTokenSource();
        _tcpClient = new TcpClient();
        await _tcpClient.ConnectAsync(ipAddress, port, ct);
        _stream = _tcpClient.GetStream();
        _bufferCount = 0;
        LogMessage?.Invoke(this, $"已连接压力控制器 {ipAddress}:{port}");
        ConnectionStateChanged?.Invoke(this, true);
        _ = Task.Run(() => ReceiveLoop(_cts.Token), _cts.Token);
    }

    public async Task DisconnectAsync()
    {
        _cts?.Cancel();
        _tcpClient?.Close(); _stream?.Dispose();
        _tcpClient = null; _stream = null;
        _cts?.Dispose(); _cts = null;
        _bufferCount = 0;
        LogMessage?.Invoke(this, "压力控制器已断开");
        ConnectionStateChanged?.Invoke(this, false);
        await Task.CompletedTask;
    }

    public async Task<float> ReadParameterAsync(ParameterId paramId, CancellationToken ct = default)
    {
        if (!IsConnected) throw new InvalidOperationException("设备未连接");
        byte[] frame = ProtocolEncoder.EncodeReadParamRequest(paramId);
        using var lcts = CancellationTokenSource.CreateLinkedTokenSource(ct, _cts!.Token);
        var tcs = new TaskCompletionSource<DecodeResult>();
        _pendingResponse = tcs;
        await _stream!.WriteAsync(frame, lcts.Token);
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var fcts = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, lcts.Token);
            await using (fcts.Token.Register(() => tcs.TrySetCanceled()))
            {
                var r = await tcs.Task;
                if (r.FrameType == FrameType.ReadParamResponse && r.ParameterValue.HasValue)
                    return r.ParameterValue.Value;
            }
        }
        catch (OperationCanceledException) { throw new TimeoutException($"读参超时:{paramId}"); }
        finally { _pendingResponse = null; }
        throw new InvalidOperationException($"读参失败:{paramId}");
    }

    public async Task<bool> WriteParameterAsync(ParameterId paramId, float value, CancellationToken ct = default)
    {
        if (!IsConnected) throw new InvalidOperationException("设备未连接");
        byte[] frame = ProtocolEncoder.EncodeWriteParamRequest(paramId, value);
        using var lcts = CancellationTokenSource.CreateLinkedTokenSource(ct, _cts!.Token);
        var tcs = new TaskCompletionSource<DecodeResult>();
        _pendingResponse = tcs;
        await _stream!.WriteAsync(frame, lcts.Token);
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var fcts = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, lcts.Token);
            await using (fcts.Token.Register(() => tcs.TrySetCanceled()))
            {
                var r = await tcs.Task;
                if (r.FrameType == FrameType.WriteParamResponse && r.Success.HasValue)
                    return r.Success.Value;
            }
        }
        catch (OperationCanceledException) { throw new TimeoutException($"写参超时:{paramId}"); }
        finally { _pendingResponse = null; }
        return false;
    }

    private async Task ReceiveLoop(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && IsConnected)
            {
                int read = await _stream!.ReadAsync(_receiveBuffer.AsMemory(_bufferCount, _receiveBuffer.Length - _bufferCount), ct);
                if (read == 0) break;
                _bufferCount += read;
                int offset = 0, count = _bufferCount;
                while (count >= ProtocolConstants.MinFrameLength)
                {
                    var r = ProtocolDecoder.TryDecode(_receiveBuffer, offset, count, out int consumed);
                    if (r != null) { offset += consumed; count -= consumed; ProcessFrame(r); }
                    else if (consumed > 0) { offset += consumed; count -= consumed; }
                    else break;
                }
                if (count > 0 && offset > 0) Array.Copy(_receiveBuffer, offset, _receiveBuffer, 0, count);
                _bufferCount = count;
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (Exception ex) { LogMessage?.Invoke(this, $"接收异常:{ex.Message}"); }
        finally { await DisconnectAsync(); }
    }

    private void ProcessFrame(DecodeResult r)
    {
        switch (r.FrameType)
        {
            case FrameType.RealTimeData: TelemetryDataReceived?.Invoke(this, r.TelemetryData!); break;
            case FrameType.ReadParamResponse:
            case FrameType.WriteParamResponse: _pendingResponse?.TrySetResult(r); break;
        }
    }

    public void Dispose() { _cts?.Cancel(); _stream?.Dispose(); _tcpClient?.Dispose(); _cts?.Dispose(); }
}
