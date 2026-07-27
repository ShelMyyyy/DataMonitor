using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace DataMonitor.Converters;

public class BoolToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool isConnected && isConnected)
            return new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
        return new SolidColorBrush(Color.FromRgb(0xE0, 0x3E, 0x3E));
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class StatusToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value?.ToString() switch
        {
            "正常" => new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)),
            "告警" => new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00)),
            "故障" => new SolidColorBrush(Color.FromRgb(0xE0, 0x3E, 0x3E)),
            _ => new SolidColorBrush(Colors.Gray)
        };
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class BoolInvertConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : true;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : true;
}

public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && b ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// 反向布尔可见性：true→Collapsed, false→Visible
/// </summary>
public class BoolToVisibilityInvertConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && b ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// 集合计数 → 可见性。参数 "invert" 时取反（0=Visible）。
/// </summary>
public class HasItemsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool invert = parameter is string s && s == "invert";
        if (value is int count)
        {
            bool hasItems = count > 0;
            return (invert ? !hasItems : hasItems) ? Visibility.Visible : Visibility.Collapsed;
        }
        return invert ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
