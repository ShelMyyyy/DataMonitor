using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using DataMonitor.Core.Models;

namespace DataMonitor.ViewModels;

/// <summary>
/// 通道视图模型 — 管理单个数据通道的显示值、图表数据和可见性。
/// 每个通道对应设备的一个遥测指标（如温度、压力），包含实时数值和折线图。
/// </summary>
public partial class ChannelViewModel : ObservableObject
{
    /// <summary>图表保留的最大历史数据点数</summary>
    private const int MaxHistory = 60;

    /// <summary>通道标签（如"温度"）</summary>
    public string Label => _def.Label;

    /// <summary>通道单位（如"°C"）</summary>
    public string Unit => _def.Unit;

    /// <summary>对应的遥测属性名（用于反射取值）</summary>
    public string PropertyName => _def.PropertyName;

    /// <summary>当前显示值字符串</summary>
    [ObservableProperty]
    private string _displayValue = "--";

    /// <summary>是否在图表中可见</summary>
    [ObservableProperty]
    private bool _isChartVisible = true;

    /// <summary>图表数据点（LiveCharts2 绑定用）</summary>
    public ObservableCollection<ObservablePoint> ChartValues { get; } = new();

    /// <summary>折线系列集合（LiveCharts2 绑定）</summary>
    public ISeries[] ChartSeries { get; }

    /// <summary>X轴配置：显示浅色分隔线，隐藏标签（数据点序号无实际意义）</summary>
    public Axis[] XAxes { get; } = 
    { 
        new Axis 
        { 
            ShowSeparatorLines = true,
            SeparatorsPaint = new SolidColorPaint(new SKColor(230, 230, 230)),
            LabelsPaint = new SolidColorPaint(new SKColor(180, 180, 180)),
            TextSize = 10
        } 
    };

    /// <summary>Y轴配置：显示数值标签和浅色分隔线，实时反映数据范围</summary>
    public Axis[] YAxes { get; } = 
    { 
        new Axis 
        { 
            ShowSeparatorLines = true,
            SeparatorsPaint = new SolidColorPaint(new SKColor(230, 230, 230)),
            LabelsPaint = new SolidColorPaint(new SKColor(120, 120, 120)),
            TextSize = 10,
            MinLimit = null  // 让 LiveCharts2 自动计算范围
        } 
    };

    private readonly DataChannelDef _def;

    /// <summary>
    /// 构造函数 — 基于 DataChannelDef 创建通道 VM
    /// </summary>
    /// <param name="def">Core 层定义的通道元数据</param>
    public ChannelViewModel(DataChannelDef def)
    {
        _def = def;

        // 配置 LiveCharts2 折线系列
        ChartSeries = new ISeries[]
        {
            new LineSeries<ObservablePoint>
            {
                Values = ChartValues,
                Fill = null,
                Stroke = new SolidColorPaint(SKColor.Parse("#4A90D9"), 2),
                GeometrySize = 0,
                LineSmoothness = 0.3
            }
        };
    }

    /// <summary>
    /// 添加一个数据点到图表中，同时更新显示的实时数值。
    /// </summary>
    /// <param name="value">新的测量值</param>
    public void AddDataPoint(double value)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            int x = ChartValues.Count > 0 ? (int)ChartValues[^1].X! + 1 : 0;
            ChartValues.Add(new ObservablePoint(x, value));

            while (ChartValues.Count > MaxHistory)
                ChartValues.RemoveAt(0);

            // 格式化实时数值：状态通道显示中文，数值通道保留1位小数
            if (PropertyName == "StatusText")
            {
                DisplayValue = (int)value switch { 0 => "正常", 1 => "告警", 2 => "故障", _ => "未知" };
            }
            else
            {
                DisplayValue = $"{value:F1}";
            }
        });
    }
}
