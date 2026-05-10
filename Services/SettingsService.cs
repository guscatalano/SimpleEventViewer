using Microsoft.UI.Xaml;
using Windows.Storage;

namespace SimpleEventViewer_WinUI.Services;

public enum AppTheme
{
    System = 0,
    Light = 1,
    Dark = 2
}

public enum RowColorMode
{
    Badge = 0,
    FullRow = 1
}

public class SettingsService
{
    private static readonly Lazy<SettingsService> _instance = new(() => new SettingsService());
    public static SettingsService Instance => _instance.Value;

    private const string ThemeKey = "AppTheme";
    private const string AccentColorKey = "AccentColor";
    private const string RowColorModeKey = "RowColorMode";

    public event Action? ThemeChanged;

    private SettingsService() { }

    public RowColorMode RowColorMode
    {
        get
        {
            try
            {
                var localSettings = ApplicationData.Current.LocalSettings;
                if (localSettings.Values[RowColorModeKey] is int modeValue)
                {
                    return (RowColorMode)modeValue;
                }
            }
            catch { }
            return RowColorMode.Badge;
        }
        set
        {
            try
            {
                var localSettings = ApplicationData.Current.LocalSettings;
                localSettings.Values[RowColorModeKey] = (int)value;
            }
            catch { }
            ThemeChanged?.Invoke();
        }
    }

    public AppTheme Theme
    {
        get
        {
            try
            {
                var localSettings = ApplicationData.Current.LocalSettings;
                if (localSettings.Values[ThemeKey] is int themeValue)
                {
                    return (AppTheme)themeValue;
                }
            }
            catch { }
            return AppTheme.System;
        }
        set
        {
            try
            {
                var localSettings = ApplicationData.Current.LocalSettings;
                localSettings.Values[ThemeKey] = (int)value;
            }
            catch { }
            ApplyTheme(value);
            ThemeChanged?.Invoke();
        }
    }

    public string AccentColor
    {
        get
        {
            try
            {
                var localSettings = ApplicationData.Current.LocalSettings;
                if (localSettings.Values[AccentColorKey] is string color)
                {
                    return color;
                }
            }
            catch { }
            return "Default";
        }
        set
        {
            try
            {
                var localSettings = ApplicationData.Current.LocalSettings;
                localSettings.Values[AccentColorKey] = value;
            }
            catch { }
            ThemeChanged?.Invoke();
        }
    }

    public void ApplyTheme(AppTheme theme)
    {
        if (Application.Current is App app && app.MainWindow?.Content is FrameworkElement element)
        {
            element.RequestedTheme = theme switch
            {
                AppTheme.Light => ElementTheme.Light,
                AppTheme.Dark => ElementTheme.Dark,
                _ => ElementTheme.Default
            };
        }
    }
}
