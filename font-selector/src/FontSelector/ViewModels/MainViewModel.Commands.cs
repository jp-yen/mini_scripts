using System;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FontSelector.Models;

namespace FontSelector.ViewModels;

public partial class MainViewModel
{
    // ═══════════════════════════════════════════════════════
    // Status bar & Theme
    // ═══════════════════════════════════════════════════════

    public static readonly string[] AvailableThemeKeys = ["tal7aouy", "spinel", "dark", "light"];

    public string CurrentThemeDisplayName => CurrentTheme switch
    {
        "tal7aouy" => "Theme (Tal7aouy)",
        "spinel" => "Spinel (Shopify)",
        "dark" => "Dark (Classic)",
        "light" => "Light",
        _ => CurrentTheme
    };

    [RelayCommand]
    public void ToggleTheme()
    {
        var currentIndex = Array.IndexOf(AvailableThemeKeys, CurrentTheme);
        var nextIndex = (currentIndex + 1) % AvailableThemeKeys.Length;
        SetTheme(AvailableThemeKeys[nextIndex]);
    }

    [RelayCommand]
    public void SetTheme(string themeKey)
    {
        if (Array.IndexOf(AvailableThemeKeys, themeKey) < 0) return;
        _themeService.SetTheme(themeKey);
        CurrentTheme = _themeService.CurrentTheme;
        IsDarkTheme = _themeService.IsDarkTheme;
        OnPropertyChanged(nameof(CurrentThemeDisplayName));
        StatusText = $"🎨 テーマを切り替えました: {CurrentThemeDisplayName}";
    }

    // ═══════════════════════════════════════════════════════
    // クリップボードコピーコマンド
    // ═══════════════════════════════════════════════════════

    [RelayCommand]
    private void CopyFontFamily(object? target)
    {
        var familyName = target switch
        {
            FontFamilyInfo family => family.FamilyName,
            TypefaceInfo typeface => typeface.FamilyName,
            _ => SelectedFamily?.FamilyName ?? SelectedTypeface?.FamilyName
        };
        if (string.IsNullOrWhiteSpace(familyName)) return;

        CopyToClipboard(familyName);
        StatusText = $"📋 フォントファミリー名をコピーしました: {familyName}";
    }

    [RelayCommand]
    private void CopyFontName(object? target)
    {
        var family = SelectedFamily?.FamilyName ?? SelectedTypeface?.FamilyName;
        var style = SelectedTypeface?.StyleName;
        var fontName = target switch
        {
            FontFamilyInfo f => $"{f.FamilyName} {f.DefaultTypeface.StyleName}".Trim(),
            TypefaceInfo typeface => string.IsNullOrWhiteSpace(typeface.StyleName) ? typeface.FamilyName : $"{typeface.FamilyName} {typeface.StyleName}".Trim(),
            _ => !string.IsNullOrWhiteSpace(style) && !string.IsNullOrWhiteSpace(family) ? $"{family} {style}".Trim() : family
        };
        if (string.IsNullOrWhiteSpace(fontName)) return;

        CopyToClipboard(fontName);
        StatusText = $"📋 フォント名をコピーしました: {fontName}";
    }

    [RelayCommand]
    private void CopyCss(object? target)
    {
        var (familyName, category, spacing) = target switch
        {
            FontFamilyInfo family => (family.FamilyName, family.Category, family.Spacing),
            TypefaceInfo typeface => (typeface.FamilyName, typeface.Category, typeface.Spacing),
            _ => (SelectedFamily?.FamilyName ?? SelectedTypeface?.FamilyName,
                  SelectedTypeface?.Category ?? SelectedFamily?.Category,
                  SelectedTypeface?.Spacing ?? SelectedFamily?.Spacing)
        };
        if (string.IsNullOrWhiteSpace(familyName)) return;

        var genericFallback = category switch
        {
            "明朝 / セリフ" => "serif",
            "ゴシック / サンセリフ" => "sans-serif",
            "等幅" => "monospace",
            "手書き / 筆記体" => "cursive",
            "装飾 / 見出し" => "fantasy",
            _ => spacing == "等幅" ? "monospace" : "sans-serif"
        };

        var css = $"font-family: '{familyName}', {genericFallback};";
        CopyToClipboard(css);
        StatusText = $"📋 CSS 指定をコピーしました: {css}";
    }

    [RelayCommand]
    private void CopyFilePath(object? target)
    {
        var path = target switch
        {
            FontFamilyInfo family => family.FilePath,
            TypefaceInfo typeface => typeface.FilePath,
            _ => SelectedTypeface?.FilePath ?? SelectedFamily?.FilePath
        };
        if (string.IsNullOrWhiteSpace(path)) return;

        CopyToClipboard(path);
        StatusText = $"📋 ファイルパスをコピーしました: {path}";
    }

    private static void CopyToClipboard(string text)
    {
        try
        {
            Clipboard.SetDataObject(text, true);
        }
        catch
        {
            // Windows のクリップボードアクセス競合を無視
        }
    }
}
