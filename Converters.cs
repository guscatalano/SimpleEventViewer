using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

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
