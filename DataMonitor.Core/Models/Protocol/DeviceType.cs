namespace DataMonitor.Core.Models.Protocol;

/// <summary>
/// 设备类型定义
/// 用于设备发现时识别下位机的硬件类型。
/// 不同类型的设备有不同的默认参数集和端口。
/// </summary>
public enum DeviceType : byte
{
    /// <summary>通用传感器设备（默认端口8888）</summary>
    GeneralSensor = 0x01,

    /// <summary>温度监测仪（默认端口8889）</summary>
    TemperatureMonitor = 0x02,

    /// <summary>压力控制器（默认端口8890）</summary>
    PressureController = 0x03
}
