using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace SimpleEventViewer.Services;

/// <summary>
/// Applies the selected color-scheme app-wide so the user's choice drives the
/// accent color used by buttons, focus rings, hyperlinks, the DataGrid
/// selection highlight, and any other control that consumes
/// <c>SystemAccentColor</c> / <c>AccentFillColor*Brush</c>.
///
/// Two mechanisms are used together:
///   1. <see cref="ColorPaletteResources"/> merged into the Application
///      resources — this propagates to the rest of the visual tree.
///   2. Direct mutation of the named accent brushes that controls hold a
///      strong reference to (their <see cref="SolidColorBrush.Color"/>) so
///      already-rendered controls update without a relaunch.
/// </summary>
public static class AccentTheme
{
    private static ColorPaletteResources? _activePalette;

    // Snapshot of the WinUI defaults for the brushes we mutate. Captured the
    // first time ApplyTheme runs so we can restore them when the user picks
    // a non-overriding preset (System default / Light / Dark) after using a
    // curated preset.
    private static readonly Dictionary<string, Color> _defaultBrushColors = new();

    private static readonly string[] _trackedBrushes = new[]
    {
        "ApplicationPageBackgroundThemeBrush",
        "LayerFillColorDefaultBrush",
        "CardStrokeColorDefaultBrush",
        "SubtleFillColorSecondaryBrush",
        "TextFillColorPrimaryBrush",
        "TextFillColorSecondaryBrush"
    };

    private static void CaptureDefaultsIfNeeded()
    {
        var app = Application.Current;
        if (app == null) return;
        // Capture per-brush rather than gating on a single flag — some
        // theme resources may not be hydrated on the very first call but
        // appear later. Once captured we don't overwrite.
        foreach (var key in _trackedBrushes)
        {
            if (_defaultBrushColors.ContainsKey(key)) continue;
            if (app.Resources.TryGetValue(key, out var obj) && obj is SolidColorBrush brush)
            {
                _defaultBrushColors[key] = brush.Color;
            }
        }
    }

    /// <summary>
    /// Applies the surface colors carried by a theme preset (page background,
    /// card background, etc.). If the preset doesn't specify a value for a
    /// given brush, the WinUI default captured at startup is restored — this
    /// keeps switching back to System/Light/Dark from Nord/Dracula/etc. clean.
    /// </summary>
    public static void ApplyPresetSurfaces(ThemePresets.Preset preset)
    {
        try
        {
            var app = Application.Current;
            if (app == null) return;
            CaptureDefaultsIfNeeded();

            Set("ApplicationPageBackgroundThemeBrush", preset.PageBackground);
            Set("LayerFillColorDefaultBrush", preset.CardBackground);
            Set("CardStrokeColorDefaultBrush", preset.CardStroke);
            Set("SubtleFillColorSecondaryBrush", preset.SubtleFill);
            Set("TextFillColorPrimaryBrush", preset.TextPrimary);
            Set("TextFillColorSecondaryBrush", preset.TextSecondary);

            void Set(string key, Color? value)
            {
                Color target = value ?? (_defaultBrushColors.TryGetValue(key, out var def) ? def : default);
                if (target == default && !value.HasValue) return; // no default captured, leave alone
                UpdateBrushColor(app.Resources, key, target);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AccentTheme] ApplyPresetSurfaces failed: {ex}");
        }
    }

    /// <summary>
    /// Apply the named color scheme to the current Application. Safe to call
    /// during startup (before any window is shown) or while the app is running.
    /// </summary>
    public static void ApplyToApplication(string scheme)
    {
        ApplyColor(scheme, scheme == "Default" ? (Color?)null : ColorSchemes.GetAccentColor(scheme));
    }

    /// <summary>
    /// Apply a literal accent color. Used by theme presets (Nord, Dracula, …)
    /// where the accent isn't one of the named color-scheme entries.
    /// </summary>
    public static void ApplyAccent(Color color)
    {
        ApplyColor(scheme: null, accent: color);
    }

    private static void ApplyColor(string? scheme, Color? accent)
    {
        try
        {
            var app = Application.Current;
            if (app == null) return;

            // 1) Replace any prior palette merge with one keyed off the new accent.
            if (_activePalette != null)
            {
                app.Resources.MergedDictionaries.Remove(_activePalette);
                _activePalette = null;
            }

            if (accent.HasValue)
            {
                _activePalette = new ColorPaletteResources { Accent = accent.Value };
                app.Resources.MergedDictionaries.Add(_activePalette);
            }

            // 2) Mutate the named accent brushes on the existing Application
            // resource dictionary. Controls hold the brush instance by reference,
            // so this updates them in-place without needing a relaunch. When
            // accent is null (Default scheme), reset to the Windows system accent.
            var effective = accent ?? GetSystemAccent();
            UpdateBrushColor(app.Resources, "AccentFillColorDefaultBrush", effective);
            UpdateBrushColor(app.Resources, "AccentFillColorSecondaryBrush", WithAlpha(effective, 0xE6));
            UpdateBrushColor(app.Resources, "AccentFillColorTertiaryBrush", WithAlpha(effective, 0xCC));
            UpdateBrushColor(app.Resources, "AccentTextFillColorPrimaryBrush", effective);
            UpdateBrushColor(app.Resources, "AccentTextFillColorSecondaryBrush", effective);
            UpdateBrushColor(app.Resources, "AccentTextFillColorTertiaryBrush", effective);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AccentTheme] ApplyColor failed: {ex}");
        }
    }

    private static Color GetSystemAccent()
    {
        try
        {
            var settings = new Windows.UI.ViewManagement.UISettings();
            return settings.GetColorValue(Windows.UI.ViewManagement.UIColorType.Accent);
        }
        catch
        {
            return Color.FromArgb(255, 0, 120, 212);
        }
    }

    private static void UpdateBrushColor(ResourceDictionary res, string key, Color color)
    {
        if (res.TryGetValue(key, out var obj) && obj is SolidColorBrush brush)
        {
            brush.Color = color;
        }
    }

    private static Color WithAlpha(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);
}
