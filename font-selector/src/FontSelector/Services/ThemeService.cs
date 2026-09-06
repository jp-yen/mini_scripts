using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace FontSelector.Services;

/// <summary>
/// Manages application theme states, ResourceDictionary swapping,
/// WebView2 preview colors, and native Windows 11 DWM title bar styling.
/// </summary>
public class ThemeService : IThemeService
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_CAPTION_COLOR = 35;
    private const int DWMWA_TEXT_COLOR = 36;

    public string CurrentTheme { get; private set; } = "dark";

    public bool IsDarkTheme => CurrentTheme != "light";

    public event Action<string>? ThemeChanged;

    public void SetTheme(string themeKey)
    {
        if (string.IsNullOrWhiteSpace(themeKey))
            themeKey = "dark";

        CurrentTheme = themeKey.ToLowerInvariant();

        var themeFile = CurrentTheme switch
        {
            "tal7aouy" => "Themes/Tal7aouyTheme.xaml",
            "spinel" => "Themes/SpinelTheme.xaml",
            "light" => "Themes/LightTheme.xaml",
            _ => "Themes/DarkTheme.xaml"
        };

        if (Application.Current is not null)
        {
            var newTheme = new ResourceDictionary { Source = new Uri(themeFile, UriKind.Relative) };
            Application.Current.Resources.MergedDictionaries.Clear();
            Application.Current.Resources.MergedDictionaries.Add(newTheme);
        }

        ThemeChanged?.Invoke(CurrentTheme);
    }

    public void ApplyTitleBarTheme(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;

        Color bgColor = Color.FromRgb(0x21, 0x25, 0x2B);
        Color textColor = Color.FromRgb(0xE6, 0xED, 0xF3);

        if (Application.Current is not null)
        {
            if (Application.Current.TryFindResource("WindowBackgroundColor") is Color c)
                bgColor = c;
            else if (Application.Current.TryFindResource("WindowBackground") is SolidColorBrush b)
                bgColor = b.Color;

            if (Application.Current.TryFindResource("PrimaryTextColor") is Color tc)
                textColor = tc;
            else if (Application.Current.TryFindResource("PrimaryText") is SolidColorBrush tb)
                textColor = tb.Color;
        }

        int darkModeVal = IsDarkTheme ? 1 : 0;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkModeVal, sizeof(int));
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref darkModeVal, sizeof(int));

        // Windows 11 (build 22000+) custom caption background & text colors
        int captionColor = (bgColor.B << 16) | (bgColor.G << 8) | bgColor.R;
        DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref captionColor, sizeof(int));

        int textClr = (textColor.B << 16) | (textColor.G << 8) | textColor.R;
        DwmSetWindowAttribute(hwnd, DWMWA_TEXT_COLOR, ref textClr, sizeof(int));
    }

    public string GetPreviewTextColor(string? themeKey = null)
    {
        return (themeKey ?? CurrentTheme) switch
        {
            "tal7aouy" => "#e6edf3",
            "spinel" => "#f0ecfc",
            "light" => "#0f172a",
            _ => "#f1f5f9"
        };
    }
}
