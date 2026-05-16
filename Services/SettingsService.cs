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

public enum TitleFormat
{
    /// <summary>"sample.evtx — Simple Event Viewer". The default.</summary>
    SourceThenApp = 0,
    /// <summary>"Simple Event Viewer".</summary>
    JustApp = 1,
    /// <summary>"Simple Event Viewer — sample.evtx".</summary>
    AppThenSource = 2
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
    private const string ColumnVisibilityKey = "ColumnVisibility";
    private const string FilterVisibilityKey = "FilterVisibility";
    private const string DetailFieldVisibilityKey = "DetailFieldVisibility";
    private const string McpServerEnabledKey = "McpServerEnabled";
    private const string McpServerPortKey = "McpServerPort";
    private const string ExperimentalFormatsKey = "ExperimentalFileFormats";
    private const string MultiSelectKeyPrefix = "FilterMultiSelect_";
    private const string TitleFormatKey = "TitleFormat";

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

    /// <summary>
    /// Returns the persisted visible/hidden state of each column keyed by its
    /// Tag, or null if the user has never toggled the Columns menu. Format
    /// on disk is "tag=0|1;..." (1 = visible).
    /// </summary>
    public Dictionary<string, bool>? LoadColumnVisibility()
    {
        try
        {
            if (ApplicationData.Current.LocalSettings.Values[ColumnVisibilityKey] is string s)
            {
                var result = new Dictionary<string, bool>();
                foreach (var part in s.Split(';', StringSplitOptions.RemoveEmptyEntries))
                {
                    var eq = part.IndexOf('=');
                    if (eq <= 0) continue;
                    var key = part.Substring(0, eq);
                    var val = part.Substring(eq + 1);
                    result[key] = val == "1" || val.Equals("true", StringComparison.OrdinalIgnoreCase);
                }
                return result;
            }
        }
        catch { }
        return null;
    }

    public void SaveColumnVisibility(IReadOnlyDictionary<string, bool> visibility)
    {
        try
        {
            var s = string.Join(";", visibility.Select(kv => $"{kv.Key}={(kv.Value ? 1 : 0)}"));
            ApplicationData.Current.LocalSettings.Values[ColumnVisibilityKey] = s;
        }
        catch { }
        ColumnVisibilityChanged?.Invoke();
    }

    /// <summary>Fired after column-visibility settings change so the active
    /// MainPage can refresh its DataGrid without a relaunch.</summary>
    public event Action? ColumnVisibilityChanged;

    /// <summary>
    /// The full list of DataGrid column identifiers in display order, with a
    /// human-readable label and the default visibility used the first time
    /// a user launches the app. Kept here (rather than spread across XAML)
    /// so Settings can render checkboxes for them without duplicating the list.
    /// </summary>
    public Dictionary<string, bool>? LoadFilterVisibility()
    {
        try
        {
            if (ApplicationData.Current.LocalSettings.Values[FilterVisibilityKey] is string s)
            {
                var result = new Dictionary<string, bool>();
                foreach (var part in s.Split(';', StringSplitOptions.RemoveEmptyEntries))
                {
                    var eq = part.IndexOf('=');
                    if (eq <= 0) continue;
                    var key = part.Substring(0, eq);
                    var val = part.Substring(eq + 1);
                    result[key] = val == "1" || val.Equals("true", StringComparison.OrdinalIgnoreCase);
                }
                return result;
            }
        }
        catch { }
        return null;
    }

    public void SaveFilterVisibility(IReadOnlyDictionary<string, bool> visibility)
    {
        try
        {
            var s = string.Join(";", visibility.Select(kv => $"{kv.Key}={(kv.Value ? 1 : 0)}"));
            ApplicationData.Current.LocalSettings.Values[FilterVisibilityKey] = s;
        }
        catch { }
        FilterVisibilityChanged?.Invoke();
        DetailFieldVisibilityChanged?.Invoke();
    }

    public event Action? FilterVisibilityChanged;

    public Dictionary<string, bool>? LoadDetailFieldVisibility()
    {
        try
        {
            if (ApplicationData.Current.LocalSettings.Values[DetailFieldVisibilityKey] is string s)
            {
                var result = new Dictionary<string, bool>();
                foreach (var part in s.Split(';', StringSplitOptions.RemoveEmptyEntries))
                {
                    var eq = part.IndexOf('=');
                    if (eq <= 0) continue;
                    var key = part.Substring(0, eq);
                    var val = part.Substring(eq + 1);
                    result[key] = val == "1" || val.Equals("true", StringComparison.OrdinalIgnoreCase);
                }
                return result;
            }
        }
        catch { }
        return null;
    }

    public void SaveDetailFieldVisibility(IReadOnlyDictionary<string, bool> visibility)
    {
        try
        {
            var s = string.Join(";", visibility.Select(kv => $"{kv.Key}={(kv.Value ? 1 : 0)}"));
            ApplicationData.Current.LocalSettings.Values[DetailFieldVisibilityKey] = s;
        }
        catch { }
        DetailFieldVisibilityChanged?.Invoke();
    }

    public event Action? DetailFieldVisibilityChanged;

    /// <summary>Detail-pane fields, in display order. All visible by default.</summary>
    public static readonly (string Key, string Label)[] AvailableDetailFields =
    {
        ("Id",            "Event ID"),
        ("Level",         "Level"),
        ("Time",          "Time Created"),
        ("Provider",      "Provider"),
        ("ProviderGuid",  "Provider GUID"),
        ("Channel",       "Channel"),
        ("Task",          "Task"),
        ("Keywords",      "Keywords"),
        ("User",          "User"),
        ("ProcessThread", "Process / Thread"),
        ("Computer",      "Computer"),
        ("Message",       "Message"),
        ("Xml",           "XML view"),
    };

    /// <summary>Filter sections shown in the left panel, in display order.
    /// Each section can be hidden via Settings. All visible by default.</summary>
    public static readonly (string Key, string Label)[] AvailableFilterSections =
    {
        ("Time",     "Time Range"),
        ("Source",   "Event Source"),
        ("Level",    "Event Level"),
        ("Id",       "Event ID"),
        ("Message",  "Message"),
        ("User",     "User"),
        ("Process",  "Process"),
        ("Computer", "Computer"),
        ("Channel",  "Channel"),
    };

    public static readonly (string Tag, string Label, bool DefaultVisible)[] AvailableColumns =
    {
        ("Time",    "Time",    true),
        ("Level",   "Level",   true),
        ("Id",      "ID",      true),
        ("Source",  "Source",  true),
        ("Channel", "Channel", false),
        ("User",    "User",    true),
        ("Message", "Message", true),
    };

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
            return RowColorMode.FullRow;
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

    public TitleFormat TitleFormat
    {
        get
        {
            try
            {
                if (ApplicationData.Current.LocalSettings.Values[TitleFormatKey] is int v
                    && Enum.IsDefined(typeof(TitleFormat), v))
                {
                    return (TitleFormat)v;
                }
            }
            catch { }
            return TitleFormat.SourceThenApp;
        }
        set
        {
            try { ApplicationData.Current.LocalSettings.Values[TitleFormatKey] = (int)value; } catch { }
            TitleFormatChanged?.Invoke();
        }
    }

    /// <summary>Fired when the title-format setting changes so the active
    /// window can repaint its caption.</summary>
    public event Action? TitleFormatChanged;

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

    public enum FilterDimension { Source, Level, User, Process, Computer, Channel, Id }

    public bool IsMultiSelectEnabled(FilterDimension dim)
    {
        try
        {
            if (ApplicationData.Current.LocalSettings.Values[MultiSelectKeyPrefix + dim] is bool b) return b;
        }
        catch { }
        // Defaults reflect typical usage: Source, Level, and Id are often
        // used to OR a few values together; the rest are usually "narrow to one".
        return dim == FilterDimension.Source
            || dim == FilterDimension.Level
            || dim == FilterDimension.Id;
    }

    public void SetMultiSelectEnabled(FilterDimension dim, bool value)
    {
        try { ApplicationData.Current.LocalSettings.Values[MultiSelectKeyPrefix + dim] = value; } catch { }
        MultiSelectChanged?.Invoke(dim);
    }

    /// <summary>Fired when a per-filter multi-select toggle flips so the
    /// MainPage can rebuild its ListView selection mode.</summary>
    public event Action<FilterDimension>? MultiSelectChanged;

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

    /// <summary>
    /// Reset every persisted preference to its first-launch default. Fires
    /// the change events on each affected setting so currently-open pages
    /// can rehydrate their controls without a restart.
    /// </summary>
    public void RestoreDefaults()
    {
        try
        {
            var s = ApplicationData.Current.LocalSettings.Values;
            s.Remove(ThemeKey);
            s.Remove(AccentColorKey);
            s.Remove(RowColorModeKey);
            s.Remove(MaxRowLinesKey);
            s.Remove(RememberColumnWidthsKey);
            s.Remove(ColumnWidthsKey);
            s.Remove(ColumnVisibilityKey);
            s.Remove(McpServerEnabledKey);
            s.Remove(McpServerPortKey);
            s.Remove(ExperimentalFormatsKey);
            s.Remove(TitleFormatKey);
            s.Remove(FilterVisibilityKey);
            s.Remove(DetailFieldVisibilityKey);
            foreach (FilterDimension d in Enum.GetValues(typeof(FilterDimension)))
            {
                s.Remove(MultiSelectKeyPrefix + d);
            }
        }
        catch { }

        // Re-apply theme + accent so the UI immediately reflects defaults.
        ApplyTheme(Theme);

        // Notify listeners so any currently-rendered pages refresh.
        ThemeChanged?.Invoke();
        ExperimentalFormatsChanged?.Invoke();
        ColumnVisibilityChanged?.Invoke();
        TitleFormatChanged?.Invoke();
        FilterVisibilityChanged?.Invoke();

        // Stop the MCP server if it was running — default is off.
        Mcp.EventLogMcpServer.Instance.Stop();
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
