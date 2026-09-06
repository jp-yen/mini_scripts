using System;
using System.Windows;

namespace FontSelector.Services;

public interface IThemeService
{
    string CurrentTheme { get; }
    bool IsDarkTheme { get; }
    event Action<string>? ThemeChanged;
    void SetTheme(string themeKey);
    void ApplyTitleBarTheme(Window window);
    string GetPreviewTextColor(string? themeKey = null);
}
