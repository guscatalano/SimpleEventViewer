using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SimpleEventViewer_WinUI.Services;
using Windows.UI;

namespace SimpleEventViewer_WinUI;

public sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        InitializeComponent();
        Loaded += SettingsPage_Loaded;
    }

    private void SettingsPage_Loaded(object sender, RoutedEventArgs e)
    {
        var theme = SettingsService.Instance.Theme;
        ThemeComboBox.SelectedIndex = (int)theme;

        var color = SettingsService.Instance.AccentColor;
        for (int i = 0; i < ColorSchemeComboBox.Items.Count; i++)
        {
            if (ColorSchemeComboBox.Items[i] is ComboBoxItem item && item.Tag?.ToString() == color)
            {
                ColorSchemeComboBox.SelectedIndex = i;
                break;
            }
        }

        RowColorModeComboBox.SelectedIndex = (int)SettingsService.Instance.RowColorMode;
        MaxRowLinesComboBox.SelectedIndex = SettingsService.Instance.MaxRowLines == 2 ? 1 : 0;

        UpdateSwatches(color);
    }

    private void MaxRowLinesComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MaxRowLinesComboBox.SelectedItem is ComboBoxItem item && item.Tag != null
            && int.TryParse(item.Tag.ToString(), out var lines))
        {
            SettingsService.Instance.MaxRowLines = lines;
        }
    }

    private void RowColorModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RowColorModeComboBox.SelectedItem is ComboBoxItem item && item.Tag != null)
        {
            if (int.TryParse(item.Tag.ToString(), out var modeValue))
            {
                SettingsService.Instance.RowColorMode = (RowColorMode)modeValue;
            }
        }
    }

    private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ThemeComboBox.SelectedItem is ComboBoxItem item && item.Tag != null)
        {
            if (int.TryParse(item.Tag.ToString(), out var themeValue))
            {
                SettingsService.Instance.Theme = (AppTheme)themeValue;
            }
        }
    }

    private void ColorSchemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ColorSchemeComboBox.SelectedItem is ComboBoxItem item && item.Tag != null)
        {
            var colorName = item.Tag.ToString() ?? "Default";
            SettingsService.Instance.AccentColor = colorName;
            UpdateSwatches(colorName);
        }
    }

    private void UpdateSwatches(string colorScheme)
    {
        var (critical, error, warning, info) = ColorSchemes.GetColors(colorScheme);
        CriticalSwatch.Background = new SolidColorBrush(critical);
        ErrorSwatch.Background = new SolidColorBrush(error);
        WarningSwatch.Background = new SolidColorBrush(warning);
        InfoSwatch.Background = new SolidColorBrush(info);
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack)
        {
            Frame.GoBack();
        }
        else
        {
            Frame.Navigate(typeof(MainPage));
        }
    }
}

public static class ColorSchemes
{
    public static (Color critical, Color error, Color warning, Color info) GetColors(string scheme)
    {
        return scheme switch
        {
            "Blue" => (
                Color.FromArgb(255, 0, 90, 158),
                Color.FromArgb(255, 0, 120, 212),
                Color.FromArgb(255, 144, 202, 249),
                Color.FromArgb(255, 100, 181, 246)
            ),
            "Green" => (
                Color.FromArgb(255, 27, 94, 32),
                Color.FromArgb(255, 56, 142, 60),
                Color.FromArgb(255, 174, 213, 129),
                Color.FromArgb(255, 102, 187, 106)
            ),
            "Purple" => (
                Color.FromArgb(255, 74, 20, 140),
                Color.FromArgb(255, 123, 31, 162),
                Color.FromArgb(255, 206, 147, 216),
                Color.FromArgb(255, 156, 39, 176)
            ),
            "Orange" => (
                Color.FromArgb(255, 191, 54, 12),
                Color.FromArgb(255, 230, 81, 0),
                Color.FromArgb(255, 255, 183, 77),
                Color.FromArgb(255, 255, 152, 0)
            ),
            "Red" => (
                Color.FromArgb(255, 183, 28, 28),
                Color.FromArgb(255, 211, 47, 47),
                Color.FromArgb(255, 239, 154, 154),
                Color.FromArgb(255, 244, 67, 54)
            ),
            _ => (
                Color.FromArgb(255, 196, 43, 28),
                Color.FromArgb(255, 232, 17, 35),
                Color.FromArgb(255, 252, 211, 91),
                Color.FromArgb(255, 0, 120, 212)
            )
        };
    }
}
