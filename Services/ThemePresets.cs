using Microsoft.UI.Xaml;
using Windows.UI;

namespace SimpleEventViewer.Services;

/// <summary>
/// Maps each <see cref="AppTheme"/> preset to a base ElementTheme + an
/// optional accent color. Used by SettingsService.ApplyTheme so picking
/// "Nord" or "Dracula" in the dropdown applies both the light/dark base
/// and the curated accent in a single shot.
/// </summary>
public static class ThemePresets
{
    public class Preset
    {
        /// <summary>Light/Dark/Default for ElementTheme on the root.</summary>
        public ElementTheme BaseTheme { get; init; } = ElementTheme.Default;
        /// <summary>The curated accent that ships with this preset.</summary>
        public Color? Accent { get; init; }
        /// <summary>Stable string id we use to mirror the choice into <see cref="SettingsService.AccentColor"/>.</summary>
        public string? AccentName { get; init; }
        /// <summary>True when the preset forces a specific accent (Color Scheme picker is ignored).</summary>
        public bool OverridesAccent { get; init; }

        // Optional color overrides — null means "leave WinUI's default for the base theme".
        // These four cover the visually-dominant surfaces: page background,
        // card body, card border, status-bar tint.
        public Color? PageBackground { get; init; }
        public Color? CardBackground { get; init; }
        public Color? CardStroke { get; init; }
        public Color? SubtleFill { get; init; }
        public Color? TextPrimary { get; init; }
        public Color? TextSecondary { get; init; }
    }

    public static Preset Get(AppTheme theme)
    {
        switch (theme)
        {
            case AppTheme.Light:
                return new Preset { BaseTheme = ElementTheme.Light };
            case AppTheme.Dark:
                return new Preset { BaseTheme = ElementTheme.Dark };

            case AppTheme.HighContrast:
                return new Preset
                {
                    BaseTheme = ElementTheme.Dark,
                    Accent = C(255, 215, 0), AccentName = "HighContrast", OverridesAccent = true,
                    PageBackground = C(0, 0, 0),
                    CardBackground = C(0, 0, 0),
                    CardStroke = C(255, 255, 255),
                    SubtleFill = C(20, 20, 20),
                    TextPrimary = C(255, 255, 255),
                    TextSecondary = C(255, 255, 0)
                };

            case AppTheme.Nord:
                // Nord palette — https://www.nordtheme.com/
                return new Preset
                {
                    BaseTheme = ElementTheme.Dark,
                    Accent = C(136, 192, 208), AccentName = "Nord", OverridesAccent = true,
                    PageBackground = C(46, 52, 64),    // nord0
                    CardBackground = C(59, 66, 82),    // nord1
                    CardStroke = C(76, 86, 106),       // nord3
                    SubtleFill = C(67, 76, 94),        // nord2
                    TextPrimary = C(236, 239, 244),    // nord6
                    TextSecondary = C(216, 222, 233)   // nord4
                };

            case AppTheme.Dracula:
                // Dracula — https://draculatheme.com/
                return new Preset
                {
                    BaseTheme = ElementTheme.Dark,
                    Accent = C(189, 147, 249), AccentName = "Dracula", OverridesAccent = true,
                    PageBackground = C(40, 42, 54),    // bg
                    CardBackground = C(68, 71, 90),    // current line
                    CardStroke = C(98, 114, 164),      // comment
                    SubtleFill = C(68, 71, 90),
                    TextPrimary = C(248, 248, 242),    // fg
                    TextSecondary = C(189, 147, 249)
                };

            case AppTheme.SolarizedDark:
                return new Preset
                {
                    BaseTheme = ElementTheme.Dark,
                    Accent = C(38, 139, 210), AccentName = "SolarizedDark", OverridesAccent = true,
                    PageBackground = C(0, 43, 54),     // base03
                    CardBackground = C(7, 54, 66),     // base02
                    CardStroke = C(88, 110, 117),      // base01
                    SubtleFill = C(7, 54, 66),
                    TextPrimary = C(238, 232, 213),    // base2
                    TextSecondary = C(147, 161, 161)   // base1
                };

            case AppTheme.Sepia:
                return new Preset
                {
                    BaseTheme = ElementTheme.Light,
                    Accent = C(152, 102, 51), AccentName = "Sepia", OverridesAccent = true,
                    PageBackground = C(244, 236, 216),
                    CardBackground = C(238, 229, 200),
                    CardStroke = C(208, 191, 152),
                    SubtleFill = C(228, 219, 190),
                    TextPrimary = C(91, 70, 54),
                    TextSecondary = C(120, 100, 80)
                };

            case AppTheme.System:
            default:
                return new Preset { BaseTheme = ElementTheme.Default };
        }
    }

    private static Color C(byte r, byte g, byte b) => Color.FromArgb(255, r, g, b);

    /// <summary>
    /// Human-friendly label used in the Settings dropdown.
    /// </summary>
    public static string DisplayName(AppTheme theme) => theme switch
    {
        AppTheme.System => "System default",
        AppTheme.Light => "Light",
        AppTheme.Dark => "Dark",
        AppTheme.HighContrast => "High Contrast",
        AppTheme.Nord => "Nord (dark)",
        AppTheme.Dracula => "Dracula (dark)",
        AppTheme.SolarizedDark => "Solarized (dark)",
        AppTheme.Sepia => "Sepia (light)",
        _ => theme.ToString()
    };
}
