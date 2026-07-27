using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DataMonitor.ViewModels;

namespace DataMonitor.Controls;

/// <summary>
/// 设备列表项卡片 — 可复用的 UserControl。
/// 封装设备选择交互：点击后通知 MainWindow 切换选中设备。
/// </summary>
public partial class DeviceCard : UserControl
{
    public DeviceCard()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 点击设备卡片 → 查找祖先 MainWindow → 设置 SelectedDevice
    /// 这种通过 Visual Tree 向上查找的方式是 UserControl 间通信的常见模式。
    /// </summary>
    private void OnClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not DeviceViewModel device) return;

        // 沿 Visual Tree 向上查找 MainWindow，获取 MainViewModel
        var parent = System.Windows.Media.VisualTreeHelper.GetParent(this);
        while (parent != null && parent is not Window)
            parent = System.Windows.Media.VisualTreeHelper.GetParent(parent);

        if (parent is MainWindow window && window.DataContext is MainViewModel vm)
            vm.SelectedDevice = device;
    }
}
