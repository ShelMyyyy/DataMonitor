using DataMonitor.Core.Models.Protocol;

namespace DataMonitor.Core.Models;

/// <summary>
/// 设备参数
/// 描述一个可读写参数，包含ID、名称、值、单位等元信息。
/// 提供 GetDefaultParameters() 工厂方法获取系统预定义参数集。
/// </summary>
public class DeviceParameter
{
    /// <summary>
    /// 参数唯一标识
    /// 对应通讯协议中的 ParameterId 枚举值
    /// </summary>
    public ParameterId Id { get; set; }

    /// <summary>
    /// 参数显示名称（中文）
    /// 如："目标温度"、"最大压力"
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 参数当前值
    /// 从下位机读取的实际数值
    /// </summary>
    public float Value { get; set; }

    /// <summary>
    /// 参数单位
    /// 如："°C"、"kPa"、"L/min"、"ms"
    /// </summary>
    public string Unit { get; set; } = string.Empty;

    /// <summary>
    /// 参数详细说明
    /// 描述该参数的作用和用途
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// 获取系统预定义的5个标准参数列表
    /// 这些参数在所有设备插件中通用，初始化时Value均为0，
    /// 需要调用 ReadParameterAsync 从下位机获取实际值
    /// </summary>
    /// <returns>预设参数列表</returns>
    public static List<DeviceParameter> GetDefaultParameters()
    {
        return new List<DeviceParameter>
        {
            new()
            {
                Id = ParameterId.TargetTemperature,
                Name = "目标温度",
                Value = 0,
                Unit = "°C",
                Description = "系统目标控制温度，下位机按此值调节加热/制冷输出"
            },
            new()
            {
                Id = ParameterId.MaxPressure,
                Name = "最大压力",
                Value = 0,
                Unit = "kPa",
                Description = "系统最大允许压力，超出触发安全保护机制"
            },
            new()
            {
                Id = ParameterId.FlowRateLimit,
                Name = "流量限制",
                Value = 0,
                Unit = "L/min",
                Description = "流量上限阈值，控制最大允许流速"
            },
            new()
            {
                Id = ParameterId.SampleInterval,
                Name = "采样间隔",
                Value = 0,
                Unit = "ms",
                Description = "传感器采样及数据上报的时间间隔，建议范围 100~5000ms"
            },
            new()
            {
                Id = ParameterId.AlarmThreshold,
                Name = "报警阈值",
                Value = 0,
                Unit = "°C",
                Description = "温度超限报警阈值，超过则设备进入告警状态"
            }
        };
    }
}
