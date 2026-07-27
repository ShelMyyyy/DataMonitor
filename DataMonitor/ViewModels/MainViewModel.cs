using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DataMonitor.Core.Services;

namespace DataMonitor.ViewModels;

/// <summary>
/// 主视图模型 — 应用的核心状态管理中心。
/// 负责设备扫描、设备列表管理、通道/参数的路由、日志管理和定时器。
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private string _logText = "";

    /// <summary>通讯日志文本（只读绑定，追加由 AppendLog 方法完成）</summary>
    public string LogText
    {
        get => _logText;
        set
        {
            _logText = value;
            if (_logText.Length > 12000)
                _logText = _logText[^10000..];
            OnPropertyChanged();
        }
    }

    /// <summary>是否正在扫描设备</summary>
    [ObservableProperty]
    private bool _isScanning;

    /// <summary>通讯日志面板是否可见</summary>
    [ObservableProperty]
    private bool _isLogVisible;

    /// <summary>最后更新时间显示文本</summary>
    [ObservableProperty]
    private string _lastUpdateTime = "";

    private DeviceViewModel? _selectedDevice;

    /// <summary>当前选中的设备</summary>
    public DeviceViewModel? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (SetProperty(ref _selectedDevice, value))
            {
                OnPropertyChanged(nameof(DataChannels));
                OnPropertyChanged(nameof(Parameters));
            }
        }
    }

    /// <summary>已发现的设备列表</summary>
    public ObservableCollection<DeviceViewModel> Devices { get; } = new();

    /// <summary>当前选中设备的数据通道集合（路由自 SelectedDevice）</summary>
    public ObservableCollection<ChannelViewModel> DataChannels =>
        SelectedDevice?.Channels ?? new ObservableCollection<ChannelViewModel>();

    /// <summary>当前选中设备的参数集合（路由自 SelectedDevice）</summary>
    public ObservableCollection<ParameterItem> Parameters =>
        SelectedDevice?.Parameters ?? new ObservableCollection<ParameterItem>();

    /// <summary>扫描设备命令</summary>
    public IAsyncRelayCommand ScanCommand { get; }

    /// <summary>读取全部参数命令</summary>
    public IAsyncRelayCommand ReadAllCommand { get; }

    /// <summary>切换日志面板可见性命令</summary>
    public IRelayCommand ToggleLogCommand { get; }

    /// <summary>清空通讯日志命令</summary>
    public IRelayCommand ClearLogCommand { get; }

    private readonly DispatcherTimer _timer;

    /// <summary>
    /// 构造函数 — 初始化命令和定时器
    /// </summary>
    public MainViewModel()
    {
        ScanCommand = new AsyncRelayCommand(ScanAsync);
        ReadAllCommand = new AsyncRelayCommand(ReadAllAsync);
        ToggleLogCommand = new RelayCommand(() => IsLogVisible = !IsLogVisible);
        ClearLogCommand = new RelayCommand(() => LogText = "");

        // 每 500ms 刷新最后更新时间
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Normal,
            (_, _) => LastUpdateTime = $"最后更新: {DateTime.Now:HH:mm:ss}", Application.Current.Dispatcher);
        _timer.Start();
    }

    /// <summary>
    /// 向通讯日志追加一条带时间戳的消息
    /// </summary>
    private void AppendLog(string msg)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {msg}";
        if (LogText.Length > 10000) LogText = line;
        else LogText = LogText + Environment.NewLine + line;
    }

    /// <summary>
    /// 并行扫描所有预设端口，发现下位机设备
    /// </summary>
    private async Task ScanAsync()
    {
        IsScanning = true;
        AppendLog("开始扫描设备...");
        try
        {
            var found = await DeviceDiscoverer.ScanAsync();
            Devices.Clear();
            foreach (var info in found)
            {
                var dev = new DeviceViewModel(info, AppendLog);
                Devices.Add(dev);
                AppendLog($"发现设备: {dev.DeviceName} @ {dev.Address}");
            }
            if (found.Count == 0) AppendLog("未发现任何设备");
            else if (Devices.Count > 0) SelectedDevice = Devices[0];
        }
        catch (Exception ex) { AppendLog($"扫描失败: {ex.Message}"); }
        finally { IsScanning = false; }
    }

    /// <summary>
    /// 读取当前选中设备的所有参数值
    /// </summary>
    private async Task ReadAllAsync()
    {
        if (SelectedDevice == null || !SelectedDevice.IsConnected) return;
        foreach (var p in SelectedDevice.Parameters)
            await p.ReadCommand.ExecuteAsync(null);
    }
}
