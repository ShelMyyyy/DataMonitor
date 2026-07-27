using System.Windows;

namespace DataMonitor;

/// <summary>
/// 主窗口 — 纯 UI 宿主，业务逻辑全部在 MainViewModel 中。
/// 设备选择和日志操作均已下沉到对应的 UserControl 中处理。
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }
}
