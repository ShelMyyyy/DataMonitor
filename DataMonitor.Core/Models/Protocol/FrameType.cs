namespace DataMonitor.Core.Models.Protocol;

/// <summary>
/// 通讯帧类型定义
/// 协议中每一帧的第二个字节，用于标识该帧的功能和方向。
/// 所有多字节数值均使用 Little-Endian 字节序。
/// </summary>
public enum FrameType : byte
{
    /// <summary>
    /// 实时数据上传帧（0x01）
    /// 方向：下位机 → 上位机
    /// 下位机根据采样间隔持续发送，包含温度、湿度、压力、流量等遥测数据
    /// </summary>
    RealTimeData = 0x01,

    /// <summary>
    /// 读参数请求帧（0x02）
    /// 方向：上位机 → 下位机
    /// 上位机请求读取某个设备参数，Payload 中携带目标参数ID
    /// </summary>
    ReadParamRequest = 0x02,

    /// <summary>
    /// 读参数响应帧（0x03）
    /// 方向：下位机 → 上位机
    /// 下位机返回被请求的参数ID和当前值
    /// </summary>
    ReadParamResponse = 0x03,

    /// <summary>
    /// 写参数请求帧（0x04）
    /// 方向：上位机 → 下位机
    /// 上位机请求修改某个设备参数，Payload 中携带参数ID和新值
    /// </summary>
    WriteParamRequest = 0x04,

    /// <summary>
    /// 写参数响应帧（0x05）
    /// 方向：下位机 → 上位机
    /// 下位机返回写入操作结果（成功或失败）
    /// </summary>
    WriteParamResponse = 0x05,

    /// <summary>
    /// 心跳帧（0x06）
    /// 方向：下位机 → 上位机
    /// 周期性发送，用于检测设备在线状态
    /// </summary>
    Heartbeat = 0x06,

    /// <summary>
    /// 设备信息请求帧（0x07）
    /// 方向：上位机 → 下位机
    /// 扫描时发送，Payload为空，下位机收到后应返回 InfoResponse
    /// </summary>
    InfoRequest = 0x07,

    /// <summary>
    /// 设备信息响应帧（0x08）
    /// 方向：下位机 → 上位机
    /// Payload: DeviceType(1) + FirmwareMajor(1) + FirmwareMinor(1) + ParameterCount(1)
    /// </summary>
    InfoResponse = 0x08
}
