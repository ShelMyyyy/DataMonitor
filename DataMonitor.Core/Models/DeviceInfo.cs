using DataMonitor.Core.Models.Protocol;

namespace DataMonitor.Core.Models;

/// <summary>
/// 设备发现信息
/// 下位机收到 InfoRequest 帧后返回的自身描述信息。
/// 包含设备类型、固件版本等元数据，用于上位机设备列表展示。
/// </summary>
public class DeviceInfo
{
    /// <summary>设备类型</summary>
    public DeviceType Type { get; set; }

    /// <summary>设备显示名称（如"通用传感器"）</summary>
    public string Name => Type switch
    {
        DeviceType.GeneralSensor => "通用传感器设备",
        DeviceType.TemperatureMonitor => "温度监测仪",
        DeviceType.PressureController => "压力控制器",
        _ => "未知设备"
    };

    /// <summary>固件主版本号</summary>
    public byte FirmwareMajor { get; set; }

    /// <summary>固件次版本号</summary>
    public byte FirmwareMinor { get; set; }

    /// <summary>固件版本字符串（如 v1.2）</summary>
    public string FirmwareVersion => $"v{FirmwareMajor}.{FirmwareMinor}";

    /// <summary>设备IP地址</summary>
    public string IpAddress { get; set; } = string.Empty;

    /// <summary>设备TCP端口</summary>
    public int Port { get; set; }

    /// <summary>该设备支持的参数数量</summary>
    public byte ParameterCount { get; set; }

    /// <summary>该设备类型对应的默认参数列表</summary>
    public List<DeviceParameter> GetDefaultParameters()
    {
        return Type switch
        {
            DeviceType.GeneralSensor => GeneralSensorParameters(),
            DeviceType.TemperatureMonitor => TemperatureMonitorParameters(),
            DeviceType.PressureController => PressureControllerParameters(),
            _ => new List<DeviceParameter>()
        };
    }

    // ---- 各设备类型的参数定义 ----

    private static List<DeviceParameter> GeneralSensorParameters()
    {
        return new List<DeviceParameter>
        {
            new() { Id = ParameterId.TargetTemperature, Name = "目标温度", Unit = "°C", Description = "目标控制温度" },
            new() { Id = ParameterId.MaxPressure,       Name = "最大压力", Unit = "kPa", Description = "最大允许压力" },
            new() { Id = ParameterId.FlowRateLimit,      Name = "流量限制", Unit = "L/min", Description = "流量上限阈值" },
            new() { Id = ParameterId.SampleInterval,     Name = "采样间隔", Unit = "ms", Description = "采样间隔" },
        };
    }

    private static List<DeviceParameter> TemperatureMonitorParameters()
    {
        return new List<DeviceParameter>
        {
            new() { Id = ParameterId.TargetTemperature, Name = "目标温度", Unit = "°C", Description = "目标控制温度" },
            new() { Id = ParameterId.SampleInterval,     Name = "采样间隔", Unit = "ms", Description = "采样上报间隔" }
        };
    }

    private static List<DeviceParameter> PressureControllerParameters()
    {
        return new List<DeviceParameter>
        {
            new() { Id = ParameterId.MaxPressure,   Name = "最大压力", Unit = "kPa", Description = "最大允许压力" },
            new() { Id = ParameterId.FlowRateLimit,  Name = "流量限制", Unit = "L/min", Description = "流量上限" },
            new() { Id = ParameterId.SampleInterval, Name = "采样间隔", Unit = "ms", Description = "采样上报间隔" }
        };
    }

    /// <summary>该设备类型的遥测数据通道定义（用于UI动态显示）</summary>
    public List<DataChannelDef> GetDataChannels()
    {
        return Type switch
        {
            DeviceType.GeneralSensor => new()
            {
                new("温度", "°C", "Temperature", alarmThreshold: 30.0),
                new("湿度", "%",  "Humidity",    alarmThreshold: 80.0),
                new("压力", "kPa", "Pressure",   alarmThreshold: 100.0),
                new("流量", "L/min", "FlowRate", alarmThreshold: 50.0),
                new("状态", "",   "StatusText"),
            },
            DeviceType.TemperatureMonitor => new()
            {
                new("温度", "°C", "Temperature", alarmThreshold: 30.0),
                new("状态", "",   "StatusText"),
            },
            DeviceType.PressureController => new()
            {
                new("压力", "kPa",   "Pressure", alarmThreshold: 100.0),
                new("流量", "L/min", "FlowRate", alarmThreshold: 50.0),
                new("状态", "",      "StatusText"),
            },
            _ => new()
            {
                new("温度", "°C", "Temperature", alarmThreshold: 30.0),
                new("状态", "",   "StatusText"),
            }
        };
    }
}

/// <summary>遥测数据通道定义（支持UI绑定通知）</summary>
public class DataChannelDef(string label, string unit, string propertyName, double? alarmThreshold = null) : System.ComponentModel.INotifyPropertyChanged
{
    public string Label { get; } = label;
    public string Unit { get; } = unit;
    public string PropertyName { get; } = propertyName;

    private double? _alarmThreshold = alarmThreshold;
    /// <summary>告警阈值（nullable；数值通道超过此值即告警，StatusText 通道特殊处理）</summary>
    public double? AlarmThreshold
    {
        get => _alarmThreshold;
        set
        {
            if (_alarmThreshold != value)
            {
                _alarmThreshold = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(AlarmThreshold)));
            }
        }
    }

    private string _display = "--";
    public string DisplayValue
    {
        get => _display;
        set { _display = value; PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(DisplayValue))); }
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}
