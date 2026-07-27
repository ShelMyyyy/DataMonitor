using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DataMonitor.Core.Interfaces;
using DataMonitor.Core.Models;
using DataMonitor.Core.Models.Protocol;
using DataMonitor.Protocols;

namespace DataMonitor.ViewModels;

/// <summary>
/// 设备视图模型 — 管理单个下位机设备的连接、数据接收和参数操作。
/// 负责创建对应的插件实例、绑定遥测事件、管理通道和参数集合。
/// </summary>
public partial class DeviceViewModel : ObservableObject
{
    private readonly DeviceInfo _info;

    /// <summary>设备是否已连接</summary>
    [ObservableProperty]
    private bool _isConnected;

    /// <summary>设备状态文本（"已连接"/"未连接"/遥测覆盖）</summary>
    [ObservableProperty]
    private string _statusText = "未连接";

    /// <summary>设备显示名称（如"通用传感器设备"）</summary>
    public string DeviceName => _info.Name;

    /// <summary>设备地址（IP:端口）</summary>
    public string Address => $"{_info.IpAddress}:{_info.Port}";

    /// <summary>设备通讯插件实例</summary>
    public IDevicePlugin Plugin { get; }

    /// <summary>参数项集合（每个可读写参数一个）</summary>
    public ObservableCollection<ParameterItem> Parameters { get; } = new();

    /// <summary>数据通道集合（每个遥测指标一个，含图表）</summary>
    public ObservableCollection<ChannelViewModel> Channels { get; } = new();

    /// <summary>连接命令</summary>
    public IAsyncRelayCommand ConnectCommand { get; }

    /// <summary>断开命令</summary>
    public IAsyncRelayCommand DisconnectCommand { get; }

    private readonly Action<string> _globalLog;

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="info">设备发现信息（类型、地址等）</param>
    /// <param name="globalLog">全局日志回调，消息会输出到通讯日志面板</param>
    public DeviceViewModel(DeviceInfo info, Action<string> globalLog)
    {
        _info = info;
        _globalLog = globalLog;

        // 根据设备类型创建对应的协议插件
        Plugin = info.Type switch
        {
            DeviceType.TemperatureMonitor => new TemperatureMonitorPlugin(),
            DeviceType.PressureController => new PressureControllerPlugin(),
            _ => new DefaultDevicePlugin()
        };

        // 连接状态变化 → UI更新
        Plugin.ConnectionStateChanged += (_, c) =>
            Application.Current?.Dispatcher.Invoke(() =>
            {
                IsConnected = c;
                StatusText = c ? "已连接" : "未连接";
            });

        // 通讯日志 → 全局日志
        Plugin.LogMessage += (_, msg) =>
            Application.Current?.Dispatcher.Invoke(() => _globalLog(msg));

        // 遥测数据到达 → 分发到各通道
        Plugin.TelemetryDataReceived += (_, data) =>
            Application.Current?.Dispatcher.Invoke(() => OnTelemetry(data));

        // 创建通道 ViewModel（数量因设备类型而异）
        foreach (var def in _info.GetDataChannels())
            Channels.Add(new ChannelViewModel(def));

        // 创建参数 ViewModel
        foreach (var p in _info.GetDefaultParameters())
            Parameters.Add(new ParameterItem(this, p));

        ConnectCommand = new AsyncRelayCommand(ConnectAsync);
        DisconnectCommand = new AsyncRelayCommand(DisconnectAsync);
    }

    /// <summary>
    /// 向全局日志写入一条此设备的消息
    /// </summary>
    public void Log(string msg) => _globalLog($"[{DeviceName}] {msg}");

    /// <summary>
    /// 遥测数据到达回调 — 将数据分发到对应通道进行图表更新
    /// </summary>
    private void OnTelemetry(TelemetryData data)
    {
        StatusText = data.StatusText;
        foreach (var ch in Channels)
        {
            double val = ch.PropertyName switch
            {
                "Temperature" => data.Temperature,
                "Humidity" => data.Humidity,
                "Pressure" => data.Pressure,
                "FlowRate" => data.FlowRate,
                "StatusText" => data.Status,
                _ => 0
            };
            ch.AddDataPoint(val);
        }
    }

    /// <summary>连接设备</summary>
    private async Task ConnectAsync()
    {
        try
        {
            Log("正在连接...");
            await Plugin.ConnectAsync(_info.IpAddress, _info.Port);
        }
        catch (Exception ex) { Log($"连接失败: {ex.Message}"); }
    }

    /// <summary>断开设备</summary>
    private async Task DisconnectAsync()
    {
        try { await Plugin.DisconnectAsync(); }
        catch (Exception ex) { Log($"断开异常: {ex.Message}"); }
    }
}
