using System.Windows.Controls;

namespace DataMonitor.Controls;

/// <summary>
/// 数据通道卡片 — 可复用的 UserControl。
/// 
/// 【工作原理】
/// 此控件通过 DataContext 绑定到一个 ChannelViewModel 实例，
/// 展示该通道的标签、实时数值、单位和 LiveCharts2 折线图。
/// 
/// 【如何复用】
/// 只需将 ChannelViewModel 赋值给 DataContext：
/// <code>
///   var card = new ChannelCard { DataContext = channelVm };
/// </code>
/// 或在 XAML 中：
/// <code>
///   <ItemsControl ItemsSource="{Binding DataChannels}">
///       <ItemsControl.ItemTemplate>
///           <DataTemplate>
///               <local:ChannelCard DataContext="{Binding}"/>
///           </DataTemplate>
///       </ItemsControl.ItemTemplate>
///   </ItemsControl>
/// </code>
/// 
/// 【UserControl 代码后置的特点】
/// - InitializeComponent() 加载同名 .xaml 生成的 UI
/// - 通常不写业务逻辑，逻辑放在 ViewModel 中
/// - 只在需要时处理纯 UI 事件（如拖拽、动画）
/// </summary>
public partial class ChannelCard : UserControl
{
    /// <summary>
    /// 构造函数 — 加载 XAML 定义的 UI 布局
    /// </summary>
    public ChannelCard()
    {
        InitializeComponent();
    }
}
