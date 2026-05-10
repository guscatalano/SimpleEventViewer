using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using SimpleEventViewer_WinUI.Models;
using SimpleEventViewer_WinUI.Services;
using Windows.UI;

namespace SimpleEventViewer_WinUI;

public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value != null ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

public class DateOffsetConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is DateTimeOffset dto)
        {
            return dto;
        }
        return DateTimeOffset.Now;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        if (value is DateTimeOffset dto)
        {
            return (DateTimeOffset?)dto;
        }
        return null;
    }
}

public class LevelToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is LogLevel level)
        {
            var scheme = SettingsService.Instance.AccentColor;
            var (critical, error, warning, info) = ColorSchemes.GetColors(scheme);

            return level switch
            {
                LogLevel.Critical => new SolidColorBrush(critical),
                LogLevel.Error => new SolidColorBrush(error),
                LogLevel.Warning => new SolidColorBrush(warning),
                LogLevel.Information => new SolidColorBrush(info),
                LogLevel.Verbose => new SolidColorBrush(Color.FromArgb(255, 158, 158, 158)),
                _ => new SolidColorBrush(Colors.Transparent)
            };
        }
        return new SolidColorBrush(Colors.Transparent);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

public class LevelToTextBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is LogLevel level)
        {
            // Warning has light background, needs dark text
            return level == LogLevel.Warning
                ? new SolidColorBrush(Colors.Black)
                : new SolidColorBrush(Colors.White);
        }
        return new SolidColorBrush(Colors.Black);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
