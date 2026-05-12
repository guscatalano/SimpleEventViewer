using Microsoft.UI.Xaml;
using Windows.Storage;

namespace SimpleEventViewer.Services;

public enum AppTheme
{
    System = 0,
    Light = 1,
    Dark = 2,
    // Bundled presets that pair a light/dark base with a curated accent palette.
    // The numeric values are persisted in LocalSettings so DO NOT renumber 0/1/2.
    HighContrast = 3,
    Nord = 4,
    Dracula = 5,
    SolarizedDark = 6,
    Sepia = 7
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
    private const string MaxRowLinesKey = "MaxRowLines";
    private const string RememberColumnWidthsKey = "RememberColumnWidths";
    private const string ColumnWidthsKey = "ColumnWidths";
    private const string McpServerEnabledKey = "McpServerEnabled";
    private const string McpServerPortKey = "McpServerPort";
    private const string ExperimentalFormatsKey = "ExperimentalFileFormats";

    public event Action? ThemeChanged;

    private SettingsService() { }

    private static readonly int[] AllowedRowLines = { 1, 2, 3, 4, 5, 10 };

    public bool RememberColumnWidths
    {
        get
        {
            try
            {
                var localSettings = ApplicationData.Current.LocalSettings;
                if (localSettings.Values[RememberColumnWidthsKey] is bool b) return b;
            }
            catch { }
            return true; // default on
        }
        set
        {
            try { ApplicationData.Current.LocalSettings.Values[RememberColumnWidthsKey] = value; } catch { }
        }
    }

    /// <summary>
    /// Returns a map of column tag → pixel width persisted from a previous session,
    /// or null if there's nothing stored. Format on disk is "tag=width;tag=width;..."
    /// </summary>
    public Dictionary<string, double>? LoadColumnWidths()
    {
        try
        {
            if (ApplicationData.Current.LocalSettings.Values[ColumnWidthsKey] is string s)
            {
                var result = new Dictionary<string, double>();
                foreach (var part in s.Split(';', StringSplitOptions.RemoveEmptyEntries))
                {
                    var eq = part.IndexOf('=');
                    if (eq <= 0) continue;
                    var key = part.Substring(0, eq);
                    if (double.TryParse(part.Substring(eq + 1), System.Globalization.NumberStyles.Float,
                                        System.Globalization.CultureInfo.InvariantCulture, out var w))
                    {
                        result[key] = w;
                    }
                }
                return result;
            }
        }
        catch { }
        return null;
    }

    public void SaveColumnWidths(IReadOnlyDictionary<string, double> widths)
    {
        try
        {
            var s = string.Join(";", widths.Select(kv =>
                $"{kv.Key}={kv.Value.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}"));
            ApplicationData.Current.LocalSettings.Values[ColumnWidthsKey] = s;
        }
        catch { }
    }

    public int MaxRowLines
    {
        get
        {
            try
            {
                var localSettings = ApplicationData.Current.LocalSettings;
                if (localSettings.Values[MaxRowLinesKey] is int v && Array.IndexOf(AllowedRowLines, v) >= 0)
                {
                    return v;
                }
            }
            catch { }
            return 1;
        }
        set
        {
            var clamped = Array.IndexOf(AllowedRowLines, value) >= 0 ? value : 1;
            try
            {
                var localSettings = ApplicationData.Current.LocalSettings;
                localSettings.Values[MaxRowLinesKey] = clamped;
            }
            catch { }
            ThemeChanged?.Invoke();
        }
    }

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
            AccentTheme.ApplyToApplication(value);
            ThemeChanged?.Invoke();
        }
    }

    public bool ExperimentalFileFormats
    {
        get
        {
            try
            {
                if (ApplicationData.Current.LocalSettings.Values[ExperimentalFormatsKey] is bool b) return b;
            }
            catch { }
            return false;
        }
        set
        {
            try { ApplicationData.Current.LocalSettings.Values[ExperimentalFormatsKey] = value; } catch { }
            ExperimentalFormatsChanged?.Invoke();
        }
    }

    /// <summary>Fired when the experimental-formats toggle flips, so the
    /// toolbar can show/hide the XML and ETL buttons.</summary>
    public event Action? ExperimentalFormatsChanged;

    public bool McpServerEnabled
    {
        get
        {
            try
            {
                if (ApplicationData.Current.LocalSettings.Values[McpServerEnabledKey] is bool b) return b;
            }
            catch { }
            return false;
        }
        set
        {
            try { ApplicationData.Current.LocalSettings.Values[McpServerEnabledKey] = value; } catch { }
        }
    }

    public int McpServerPort
    {
        get
        {
            try
            {
                if (ApplicationData.Current.LocalSettings.Values[McpServerPortKey] is int p && p > 0 && p < 65536) return p;
            }
            catch { }
            return 7321;
        }
        set
        {
            if (value <= 0 || value >= 65536) return;
            try { ApplicationData.Current.LocalSettings.Values[McpServerPortKey] = value; } catch { }
        }
    }

    public void ApplyTheme(AppTheme theme)
    {
        var preset = ThemePresets.Get(theme);

        if (Application.Current is App app && app.MainWindow?.Content is FrameworkElement element)
        {
            element.RequestedTheme = preset.BaseTheme;
        }

        // Surface colors first (page bg, card bg, text…). For non-overriding
        // presets, this restores the WinUI defaults captured at startup so
        // switching back from Nord/Dracula/etc. looks clean.
        AccentTheme.ApplyPresetSurfaces(preset);

        // For curated presets (Nord, Dracula, …) the bundled accent wins so
        // the look is coherent. For Light/Dark/System we leave the user's
        // explicit Color Scheme alone.
        if (preset.OverridesAccent && preset.Accent.HasValue)
        {
            AccentTheme.ApplyAccent(preset.Accent.Value);
        }
        else
        {
            AccentTheme.ApplyToApplication(AccentColor);
        }
    }
}
