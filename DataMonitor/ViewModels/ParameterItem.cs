using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DataMonitor.Core.Models;

namespace DataMonitor.ViewModels;

/// <summary>
/// 参数项视图模型 — 封装单个设备参数的显示与读写操作。
/// 支持从下位机读取参数当前值，以及写入新值到下位机。
/// </summary>
public partial class ParameterItem : ObservableObject
{
    /// <summary>参数名称（如"目标温度"）</summary>
    public string Name => _param.Name;

    /// <summary>参数单位（如"°C"）</summary>
    public string Unit => _param.Unit;

    /// <summary>参数描述文本</summary>
    public string Description => _param.Description;

    /// <summary>参数当前值的显示字符串</summary>
    [ObservableProperty]
    private string _displayValue = "--";

    /// <summary>用户输入的待写入新值</summary>
    [ObservableProperty]
    private string _editValue = "";

    /// <summary>读取参数命令</summary>
    public IAsyncRelayCommand ReadCommand { get; }

    /// <summary>写入参数命令</summary>
    public IAsyncRelayCommand WriteCommand { get; }

    private readonly DeviceViewModel _device;
    private readonly DeviceParameter _param;

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="device">所属设备 VM（提供插件和日志能力）</param>
    /// <param name="param">Core 层定义的参数元数据</param>
    public ParameterItem(DeviceViewModel device, DeviceParameter param)
    {
        _device = device;
        _param = param;
        ReadCommand = new AsyncRelayCommand(ReadAsync);
        WriteCommand = new AsyncRelayCommand(WriteAsync);
    }

    /// <summary>
    /// 从下位机读取当前参数值并更新显示
    /// </summary>
    private async Task ReadAsync()
    {
        try
        {
            var val = await _device.Plugin.ReadParameterAsync(_param.Id);
            _param.Value = val;
            DisplayValue = $"{val:F1}";
            _device.Log($"[读参] {_param.Name} = {val:F1}{_param.Unit}");
        }
        catch (Exception ex) { _device.Log($"[读参失败] {_param.Name}: {ex.Message}"); }
    }

    /// <summary>
    /// 将用户输入的 EditValue 写入下位机
    /// </summary>
    private async Task WriteAsync()
    {
        if (!float.TryParse(EditValue, out var val))
        {
            _device.Log($"[写参] 无效数值: {EditValue}");
            return;
        }
        try
        {
            var ok = await _device.Plugin.WriteParameterAsync(_param.Id, val);
            if (ok)
            {
                _param.Value = val;
                DisplayValue = $"{val:F1}";
                _device.Log($"[写参成功] {_param.Name} = {val:F1}{_param.Unit}");
            }
            else
                _device.Log($"[写参失败] {_param.Name}: 下位机拒绝");
        }
        catch (Exception ex) { _device.Log($"[写参异常] {_param.Name}: {ex.Message}"); }
    }
}
