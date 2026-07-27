namespace DataMonitor.Core.Models.Protocol;

/// <summary>
/// 设备参数ID定义
/// 每个设备参数都有一个唯一的ID，在读写参数帧的Payload中使用。
/// 参数值统一为 float 类型（4字节），不同参数有不同的物理含义和单位。
/// </summary>
public enum ParameterId : byte
{
    /// <summary>
    /// 目标温度（0x01）
    /// 类型：float | 单位：°C
    /// 系统需要维持的目标控制温度，下位机据此调节加热/制冷
    /// </summary>
    TargetTemperature = 0x01,

    /// <summary>
    /// 最大压力（0x02）
    /// 类型：float | 单位：kPa
    /// 系统允许的最大工作压力，超过此值应触发保护
    /// </summary>
    MaxPressure = 0x02,

    /// <summary>
    /// 流量限制（0x03）
    /// 类型：float | 单位：L/min
    /// 流量上限阈值，用于控制最大允许流速
    /// </summary>
    FlowRateLimit = 0x03,

    /// <summary>
    /// 采样间隔（0x04）
    /// 类型：float | 单位：ms
    /// 下位机传感器采样及数据上报的时间间隔，值越小上报越频繁
    /// </summary>
    SampleInterval = 0x04,

    /// <summary>
    /// 报警阈值（0x05）
    /// 类型：float | 单位：°C
    /// 温度超过此阈值时设备状态变为"告警"，超过1.2倍时变为"故障"
    /// </summary>
    AlarmThreshold = 0x05
}
