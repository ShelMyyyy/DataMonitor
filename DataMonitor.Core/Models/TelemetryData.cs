namespace DataMonitor.Core.Models;

/// <summary>
/// 下位机实时遥测数据
/// 由下位机通过 RealTimeData 帧周期性上传，包含完整的传感器读数。
/// 所有浮点数使用 Little-Endian 字节序编码传输。
/// </summary>
public class TelemetryData
{
    /// <summary>
    /// 采样时间戳
    /// 数据采集的时刻，使用 DateTime.Ticks 传输，本地时间
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// 当前温度
    /// 单位：°C，精度：小数点后1位
    /// </summary>
    public float Temperature { get; set; }

    /// <summary>
    /// 当前湿度
    /// 单位：%RH（相对湿度），精度：小数点后1位
    /// </summary>
    public float Humidity { get; set; }

    /// <summary>
    /// 当前压力
    /// 单位：kPa，精度：小数点后1位
    /// </summary>
    public float Pressure { get; set; }

    /// <summary>
    /// 当前流量
    /// 单位：L/min，精度：小数点后2位
    /// </summary>
    public float FlowRate { get; set; }

    /// <summary>
    /// 设备运行状态
    /// 0 = 正常, 1 = 告警（温度超过报警阈值）, 2 = 故障（温度超过1.2倍报警阈值）
    /// </summary>
    public byte Status { get; set; }

    /// <summary>
    /// 状态的中文描述文本
    /// 由 Status 字段派生，便于UI直接展示
    /// </summary>
    public string StatusText => Status switch
    {
        0 => "正常",
        1 => "告警",
        2 => "故障",
        _ => "未知"
    };
}
