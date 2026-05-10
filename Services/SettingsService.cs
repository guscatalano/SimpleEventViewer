using Microsoft.UI.Xaml;
using Windows.Storage;

namespace SimpleEventViewer_WinUI.Services;

public enum AppTheme
{
    System = 0,
    Light = 1,
    Dark = 2
}

public class SettingsService
{
    private static readonly Lazy<SettingsService> _instance = new(() => new SettingsService());
    public static SettingsService Instance => _instance.Value;

    private const string ThemeKey = "AppTheme";
    private const string AccentColorKey = "AccentColor";

    public event Action? ThemeChanged;

    private SettingsService() { }

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
