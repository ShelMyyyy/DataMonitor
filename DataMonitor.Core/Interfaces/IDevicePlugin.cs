using DataMonitor.Core.Models;
using DataMonitor.Core.Models.Protocol;

namespace DataMonitor.Core.Interfaces;

/// <summary>
/// 设备插件接口
/// 所有硬件通讯插件必须实现此接口。
/// 
/// 设计理念：
/// - 上位机不关心下位机的具体通讯方式（TCP/串口/UDP等），只依赖此接口
/// - 通过 PluginLoader 动态加载插件DLL，实现"换配置即换硬件"
/// - 使用事件模式向上位机推送实时数据和状态变化
/// </summary>
public interface IDevicePlugin : IDisposable
{
    /// <summary>
    /// 插件名称
    /// 用于日志标识和调试，对应配置文件中的 pluginName
    /// </summary>
    string PluginName { get; }

    /// <summary>
    /// 设备连接状态
    /// true=已连接，false=未连接/已断开
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// 连接到下位机设备
    /// </summary>
    /// <param name="ipAddress">设备IP地址</param>
    /// <param name="port">设备TCP端口</param>
    /// <param name="cancellationToken">取消令牌，用于超时或用户取消</param>
    /// <returns>连接完成的异步任务</returns>
    Task ConnectAsync(string ipAddress, int port, CancellationToken cancellationToken = default);

    /// <summary>
    /// 断开与下位机的连接
    /// 清理所有TCP资源和接收循环
    /// </summary>
    Task DisconnectAsync();

    /// <summary>
    /// 读取指定设备参数
    /// 发送 ReadParamRequest 帧，等待 ReadParamResponse，返回参数值
    /// </summary>
    /// <param name="paramId">要读取的参数ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>参数当前值（float）</returns>
    /// <exception cref="TimeoutException">5秒内未收到响应</exception>
    /// <exception cref="InvalidOperationException">设备未连接</exception>
    Task<float> ReadParameterAsync(ParameterId paramId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 修改指定设备参数
    /// 发送 WriteParamRequest 帧，等待 WriteParamResponse，返回操作结果
    /// </summary>
    /// <param name="paramId">要修改的参数ID</param>
    /// <param name="value">新的参数值</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>true=写入成功，false=写入失败</returns>
    /// <exception cref="TimeoutException">5秒内未收到响应</exception>
    Task<bool> WriteParameterAsync(ParameterId paramId, float value, CancellationToken cancellationToken = default);

    /// <summary>
    /// 实时遥测数据到达事件
    /// 当下位机推送 RealTimeData 帧时触发
    /// 注意：此事件在工作线程触发，订阅者需自行调度到UI线程
    /// </summary>
    event EventHandler<TelemetryData>? TelemetryDataReceived;

    /// <summary>
    /// 连接状态变化事件
    /// 参数 bool：true=已连接，false=已断开
    /// </summary>
    event EventHandler<bool>? ConnectionStateChanged;

    /// <summary>
    /// 通讯日志事件
    /// 用于调试和界面日志展示，包含所有收发的数据信息
    /// </summary>
    event EventHandler<string>? LogMessage;
}
