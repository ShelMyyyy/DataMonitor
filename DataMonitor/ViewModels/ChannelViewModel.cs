using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using DataMonitor.Core.Models;

namespace DataMonitor.ViewModels;

/// <summary>
/// 通道视图模型 — 每个通道仅有 <b>一条</b> 曲线。
/// 数据点超过自身阈值时，对应折线段自动变为红色。
/// 相邻不同颜色的分段的边界点共享，保证视觉连续。
/// </summary>
public partial class ChannelViewModel : ObservableObject
{
    private const int MaxHistory = 60;

    // ---- 预定义画刷 ----
    private static readonly SolidColorBrush NormalForeground
        = new(Color.FromRgb(0x2C, 0x3E, 0x50));   // 深蓝灰，正常状态
    private static readonly SolidColorBrush AlarmForeground
        = new(Color.FromRgb(0xE7, 0x4C, 0x3C));   // 红色，告警状态
    private static readonly SolidColorBrush FaultForeground
        = new(Color.FromRgb(0xE6, 0x7E, 0x22));   // 橙色，故障状态
    private static readonly SolidColorBrush NormalStatusForeground
        = new(Color.FromRgb(0x27, 0xAE, 0x60));   // 绿色，状态正常

    public string Label => _def.Label;
    public string Unit => _def.Unit;
    public string PropertyName => _def.PropertyName;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValueForeground))]
    private string _displayValue = "--";

    [ObservableProperty]
    private bool _isChartVisible = true;

    /// <summary>当前通道的告警阈值（支持运行时修改，修改后自动重新计算所有数据点的告警状态）</summary>
    public double? Threshold
    {
        get => _def.AlarmThreshold;
        set
        {
            if (_def.AlarmThreshold != value)
            {
                _def.AlarmThreshold = value;
                OnPropertyChanged();
                RecalculateAlarms();
            }
        }
    }

    /// <summary>是否为状态通道（StatusText 通道不显示阈值设置）</summary>
    public bool IsStatusChannel => PropertyName == "StatusText";

    /// <summary>当前最新的数据点是否超过本通道的告警阈值</summary>
    public bool IsCurrentPointAlarm => _isCurrentPointAlarm;
    private bool _isCurrentPointAlarm;

    /// <summary>实时数值前景色 — 状态通道按文字取色（正常=绿, 告警=红, 故障=橙），数值通道按超阈值状态取色</summary>
    public SolidColorBrush ValueForeground
    {
        get
        {
            if (PropertyName == "StatusText")
            {
                return DisplayValue switch
                {
                    "告警" => AlarmForeground,
                    "故障" => FaultForeground,
                    _ => NormalStatusForeground   // "正常"或其他 → 绿色
                };
            }
            return _isCurrentPointAlarm ? AlarmForeground : NormalForeground;
        }
    }

    // ---- 坐标轴 ----
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

    public Axis[] YAxes { get; } =
    {
        new Axis
        {
            Position = AxisPosition.Start,
            ShowSeparatorLines = true,
            SeparatorsPaint = new SolidColorPaint(new SKColor(230, 230, 230)),
            LabelsPaint = new SolidColorPaint(new SKColor(80, 80, 80)),
            Labeler = value => value.ToString("F1"),
            TextSize = 11,
            MinLimit = null
        }
    };

    // ========== 分段折线（单条视觉连续曲线） ==========

    private readonly DataChannelDef _def;

    /// <summary>所有已接收的数据点（按插入顺序）</summary>
    private readonly List<ObservablePoint> _allPoints = new();

    /// <summary>与 _allPoints 一一对应的告警标记</summary>
    private readonly List<bool> _pointAlarms = new();

    /// <summary>
    /// 分段列表 — 每个分段内部颜色一致。
    /// 相邻分段的边界点共享（后一段首点 = 前一段末点），
    /// 因此多条 LineSeries 在视觉上合并为一条连续折线。
    /// </summary>
    private readonly List<ObservableCollection<ObservablePoint>> _segments = new();
    private readonly List<bool> _segmentIsAlarm = new();

    private ISeries[] _chartSeries = Array.Empty<ISeries>();

    public ISeries[] ChartSeries
    {
        get => _chartSeries;
        private set
        {
            _chartSeries = value;
            OnPropertyChanged();
        }
    }

    public ChannelViewModel(DataChannelDef def)
    {
        _def = def;
    }

    /// <summary>添加数据点。自动按阈值判断告警，切换时创建新分段。</summary>
    public void AddDataPoint(double value)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            // 1. 阈值判断
            bool pointIsAlarm = IsOverThreshold(value);
            if (_isCurrentPointAlarm != pointIsAlarm)
            {
                _isCurrentPointAlarm = pointIsAlarm;
                OnPropertyChanged(nameof(ValueForeground));
            }

            // 2. 追加数据点
            int x = _allPoints.Count > 0 ? (int)_allPoints[^1].X! + 1 : 0;
            var point = new ObservablePoint(x, value);
            _allPoints.Add(point);
            _pointAlarms.Add(pointIsAlarm);

            // 3. 滑动窗口裁剪
            while (_allPoints.Count > MaxHistory)
            {
                _allPoints.RemoveAt(0);
                _pointAlarms.RemoveAt(0);
            }

            // 4. 状态切换 → 创建新分段并共享边界点
            bool alarmToggled = _pointAlarms.Count <= 1
                || _pointAlarms[^1] != _pointAlarms[^2];

            if (alarmToggled)
            {
                var newSeg = new ObservableCollection<ObservablePoint>();

                // ★ 关键：复制前一分段的末点作为首点，保证折线视觉连续
                if (_segments.Count > 0 && _segments[^1].Count > 0)
                    newSeg.Add(_segments[^1][^1]);

                _segments.Add(newSeg);
                _segmentIsAlarm.Add(pointIsAlarm);
                RebuildChartSeries();
            }

            // 5. 点追加到当前分段（ObservableCollection 自动通知 LiveCharts2）
            _segments[^1].Add(point);

            // 6. 清理因滑动窗口产生的空分段
            TrimSegments();

            // 7. 格式化显示值
            if (PropertyName == "StatusText")
            {
                DisplayValue = (int)value switch
                {
                    0 => "正常",
                    1 => "告警",
                    2 => "故障",
                    _ => "未知"
                };
            }
            else
            {
                DisplayValue = $"{value:F1}";
            }
        });
    }

    /// <summary>判断数值是否超过本通道的阈值</summary>
    private bool IsOverThreshold(double value)
    {
        // 状态通道：非0 = 告警/故障
        if (PropertyName == "StatusText")
            return (int)value != 0;

        // 数值通道：>= 阈值
        double? threshold = _def.AlarmThreshold;
        return threshold.HasValue && value >= threshold.Value;
    }

    /// <summary>阈值变更后重新计算所有数据点的告警状态并重建分段曲线</summary>
    private void RecalculateAlarms()
    {
        if (_allPoints.Count == 0) return;

        // 1. 重新计算每个点的告警状态
        for (int i = 0; i < _allPoints.Count; i++)
            _pointAlarms[i] = IsOverThreshold(_allPoints[i].Y!.Value);

        // 2. 更新当前最新点的告警状态
        bool latestAlarm = _pointAlarms[^1];
        if (_isCurrentPointAlarm != latestAlarm)
        {
            _isCurrentPointAlarm = latestAlarm;
            OnPropertyChanged(nameof(ValueForeground));
        }

        // 3. 清空并重建分段
        _segments.Clear();
        _segmentIsAlarm.Clear();

        for (int i = 0; i < _allPoints.Count; i++)
        {
            bool alarm = _pointAlarms[i];
            bool isToggle = i == 0 || alarm != _pointAlarms[i - 1];

            if (isToggle)
            {
                var newSeg = new ObservableCollection<ObservablePoint>();
                if (_segments.Count > 0 && _segments[^1].Count > 0)
                    newSeg.Add(_segments[^1][^1]);  // 共享边界点
                _segments.Add(newSeg);
                _segmentIsAlarm.Add(alarm);
            }
            _segments[^1].Add(_allPoints[i]);
        }

        RebuildChartSeries();
    }

    /// <summary>移除滑动窗口溢出后变空的头部分段</summary>
    private void TrimSegments()
    {
        while (_segments.Count > 0 && _segments[0].Count == 0)
        {
            _segments.RemoveAt(0);
            _segmentIsAlarm.RemoveAt(0);
        }

        if (_segments.Count == 0)
            ChartSeries = Array.Empty<ISeries>();
    }

    /// <summary>根据分段列表重建 ChartSeries（仅分段数变化时通知绑定）</summary>
    private void RebuildChartSeries()
    {
        // 状态通道使用 LineSmoothness=0（标准上升下降沿/台阶），数值通道使用平滑曲线
        double smoothness = PropertyName == "StatusText" ? 0 : 0.3;

        var series = new ISeries[_segments.Count];
        for (int i = 0; i < _segments.Count; i++)
        {
            bool isRed = _segmentIsAlarm[i];
            series[i] = new LineSeries<ObservablePoint>
            {
                Values = _segments[i],
                Fill = null,
                Stroke = isRed
                    ? new SolidColorPaint(SKColor.Parse("#E74C3C"), 2.5f)
                    : new SolidColorPaint(SKColor.Parse("#4A90D9"), 2),
                GeometrySize = 0,
                LineSmoothness = smoothness
            };
        }
        ChartSeries = series;
    }
}
