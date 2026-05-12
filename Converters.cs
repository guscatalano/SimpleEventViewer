using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using SimpleEventViewer.Models;
using SimpleEventViewer.Services;
using Windows.UI;

namespace SimpleEventViewer;

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

public class NullToVisibleConverter : IValueConverter
{
    // Inverse: shows when value is null
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value == null ? Visibility.Visible : Visibility.Collapsed;
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

public class SingleLineConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is string s)
        {
            return s.Replace('\n', ' ').Replace('\r', ' ').Trim();
        }
        return value ?? "";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

public class TimeSpanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is TimeSpan ts)
        {
            return (TimeSpan?)ts;
        }
        return (TimeSpan?)TimeSpan.Zero;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        if (value is TimeSpan ts)
        {
            return ts;
        }
        return TimeSpan.Zero;
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

// Returns a tinted brush for the row background when in FullRow mode, transparent in Badge mode
public class LevelToRowBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (SettingsService.Instance.RowColorMode != RowColorMode.FullRow)
        {
            return new SolidColorBrush(Colors.Transparent);
        }

        if (value is LogLevel level)
        {
            var scheme = SettingsService.Instance.AccentColor;
            var (critical, error, warning, info) = ColorSchemes.GetColors(scheme);

            // Use lower alpha for row tint so text remains readable
            byte alpha = 60;

            return level switch
            {
                LogLevel.Critical => new SolidColorBrush(Color.FromArgb(alpha, critical.R, critical.G, critical.B)),
                LogLevel.Error => new SolidColorBrush(Color.FromArgb(alpha, error.R, error.G, error.B)),
                LogLevel.Warning => new SolidColorBrush(Color.FromArgb(alpha, warning.R, warning.G, warning.B)),
                LogLevel.Information => new SolidColorBrush(Color.FromArgb(alpha, info.R, info.G, info.B)),
                LogLevel.Verbose => new SolidColorBrush(Color.FromArgb(alpha, 158, 158, 158)),
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

// Show badge only in Badge mode, collapse in FullRow mode
public class BadgeVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return SettingsService.Instance.RowColorMode == RowColorMode.Badge
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

// Show plain text level only in FullRow mode (when badge is hidden)
public class PlainLevelVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return SettingsService.Instance.RowColorMode == RowColorMode.FullRow
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
