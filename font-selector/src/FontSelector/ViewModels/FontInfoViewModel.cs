using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FontSelector.Models;

namespace FontSelector.ViewModels;

public partial class FontInfoViewModel : ObservableObject
{
    private readonly MainViewModel _mainViewModel;

    [ObservableProperty] private string _allFamilyNames = string.Empty;
    [ObservableProperty] private bool _hasWeights;
    [ObservableProperty] private IReadOnlyList<WeightTagItem> _weightTags = [];
    [ObservableProperty] private bool _hasStyles;
    [ObservableProperty] private IReadOnlyList<StyleTagItem> _styleTags = [];
    [ObservableProperty] private string _category = string.Empty;
    [ObservableProperty] private string _spacing = string.Empty;
    [ObservableProperty] private string _version = string.Empty;
    [ObservableProperty] private string _rawVersion = string.Empty;
    [ObservableProperty] private string _vendor = string.Empty;
    [ObservableProperty] private string _fileFormat = string.Empty;
    [ObservableProperty] private string _outlineFormat = string.Empty;
    [ObservableProperty] private string _fileName = string.Empty;
    [ObservableProperty] private string _filePath = string.Empty;
    [ObservableProperty] private string _installType = string.Empty;
    [ObservableProperty] private string _features = string.Empty;
    [ObservableProperty] private string _isVariableText = string.Empty;
    [ObservableProperty] private bool _hasLanguages;
    [ObservableProperty] private IReadOnlyList<string> _languageList = [];

    public FontInfoViewModel(MainViewModel mainViewModel)
    {
        _mainViewModel = mainViewModel;
    }

    private FontFamilyInfo? GetActiveFamily()
    {
        return _mainViewModel.SelectedFamily
            ?? _mainViewModel.AllFonts.FirstOrDefault(family =>
                _mainViewModel.SelectedTypeface is not null && family.Typefaces.Contains(_mainViewModel.SelectedTypeface));
    }

    [RelayCommand]
    public void SelectWeight(WeightTagItem? tag)
    {
        if (tag is null) return;

        var family = GetActiveFamily();
        if (family is null) return;

        var currentTypeface = _mainViewModel.SelectedTypeface;
        var currentStyle = currentTypeface?.Style;

        // 現在のスタイル（Italic等）と同一の書体を優先し、無ければ同ウェイトの書体を選択
        var targetTypeface = family.Typefaces.FirstOrDefault(typeface => typeface.Weight.ToOpenTypeWeight() == tag.OpenTypeWeight && typeface.Style == currentStyle)
                          ?? family.Typefaces.FirstOrDefault(typeface => typeface.Weight.ToOpenTypeWeight() == tag.OpenTypeWeight)
                          ?? tag.Typeface;

        if (targetTypeface is not null)
        {
            _mainViewModel.SelectedTypeface = targetTypeface;
            _mainViewModel.StatusText = $"⚖️ 太さを切り替えました: {targetTypeface.FamilyName} {targetTypeface.StyleName} ({tag.DisplayText})";
        }

        if (_mainViewModel.Preview is not null)
        {
            _mainViewModel.Preview.WeightValue = Math.Clamp(
                tag.OpenTypeWeight,
                _mainViewModel.Preview.WeightMin,
                _mainViewModel.Preview.WeightMax
            );
        }
    }

    [RelayCommand]
    public void SelectStyle(StyleTagItem? tag)
    {
        if (tag is null) return;

        var family = GetActiveFamily();
        if (family is null) return;

        var currentTypeface = _mainViewModel.SelectedTypeface;
        var currentWeight = currentTypeface?.Weight.ToOpenTypeWeight();

        // 現在の太さ（Regular, Bold等）と同一の書体を優先し、無ければ同スタイルの書体を選択
        var targetTypeface = family.Typefaces.FirstOrDefault(typeface => typeface.Style == tag.Style && typeface.Weight.ToOpenTypeWeight() == currentWeight)
                          ?? family.Typefaces.FirstOrDefault(typeface => typeface.Style == tag.Style)
                          ?? tag.Typeface;

        if (targetTypeface is not null)
        {
            _mainViewModel.SelectedTypeface = targetTypeface;
            _mainViewModel.StatusText = $"🎨 スタイルを切り替えました: {targetTypeface.FamilyName} {targetTypeface.StyleName} ({tag.DisplayText})";
        }
    }

    public void UpdateFromTypeface(TypefaceInfo? typeface)
    {
        if (typeface is null)
        {
            ResetMetadata();
            return;
        }

        var family = GetActiveFamily();
        AllFamilyNames = string.Join(", ", typeface.AllFamilyNames);

        // ── 太さ（Weight）タグ一覧の構築 ──
        UpdateWeightTags(family, typeface);

        // ── スタイル（Style）タグ一覧の構築 ──
        UpdateStyleTags(family, typeface);

        // ── 基本・ファイル属性 ──
        Category = typeface.Category;
        Spacing = typeface.Spacing;
        Version = typeface.Version;
        RawVersion = string.IsNullOrEmpty(typeface.RawVersion) ? typeface.Version : typeface.RawVersion;

        var vendor = !string.IsNullOrWhiteSpace(family?.Vendor) ? family.Vendor : typeface.Vendor;
        Vendor = string.IsNullOrWhiteSpace(vendor) ? "-" : vendor;
        FileFormat = typeface.FileFormat;
        OutlineFormat = typeface.OutlineFormat;
        FileName = System.IO.Path.GetFileName(typeface.FilePath);
        FilePath = typeface.FilePath;
        InstallType = typeface.InstallType;

        // ── 高度機能属性 ──
        UpdateFeatureSummary(typeface);

        // ── バリアブルフォント情報 ──
        UpdateVariableFontSummary(typeface);

        // ── 言語一覧 ──
        var languages = typeface.Languages?.ToList() ?? [];
        LanguageList = languages;
        HasLanguages = languages.Count > 0;
    }

    private void ResetMetadata()
    {
        AllFamilyNames = string.Empty;
        HasWeights = false;
        WeightTags = [];
        HasStyles = false;
        StyleTags = [];
        Category = string.Empty;
        Spacing = string.Empty;
        Version = string.Empty;
        RawVersion = string.Empty;
        Vendor = string.Empty;
        FileFormat = string.Empty;
        OutlineFormat = string.Empty;
        FileName = string.Empty;
        FilePath = string.Empty;
        InstallType = string.Empty;
        Features = string.Empty;
        IsVariableText = string.Empty;
        HasLanguages = false;
        LanguageList = [];
    }

    private void UpdateWeightTags(FontFamilyInfo? family, TypefaceInfo currentTypeface)
    {
        var distinctTypefacesByWeight = family?.Typefaces
            .GroupBy(typeface => typeface.Weight.ToOpenTypeWeight())
            .OrderBy(group => group.Key)
            .Select(group => group.First())
            .ToList() ?? [];

        if (distinctTypefacesByWeight.Count == 0)
        {
            distinctTypefacesByWeight = [currentTypeface];
        }

        var currentWeightValue = currentTypeface.Weight.ToOpenTypeWeight();
        var weightTagList = new List<WeightTagItem>(distinctTypefacesByWeight.Count);

        foreach (var typeface in distinctTypefacesByWeight)
        {
            var weightValue = typeface.Weight.ToOpenTypeWeight();
            var isSelected = weightValue == currentWeightValue;
            var japaneseLabel = GetJapaneseWeightLabel(weightValue);

            weightTagList.Add(new WeightTagItem
            {
                DisplayText = typeface.WeightDisplay,
                JapaneseWeightLabel = japaneseLabel,
                OpenTypeWeight = weightValue,
                IsSelected = isSelected,
                Typeface = typeface
            });
        }

        WeightTags = weightTagList;
        HasWeights = weightTagList.Count > 0;
    }

    private void UpdateStyleTags(FontFamilyInfo? family, TypefaceInfo currentTypeface)
    {
        var distinctTypefacesByStyle = family?.Typefaces
            .GroupBy(typeface => typeface.Style)
            .OrderBy(group => GetStyleSortOrder(group.Key))
            .Select(group => group.First())
            .ToList() ?? [];

        if (distinctTypefacesByStyle.Count == 0)
        {
            distinctTypefacesByStyle = [currentTypeface];
        }

        var currentStyle = currentTypeface.Style;
        var styleTagList = new List<StyleTagItem>(distinctTypefacesByStyle.Count);

        foreach (var typeface in distinctTypefacesByStyle)
        {
            var isSelected = typeface.Style == currentStyle;
            var japaneseLabel = GetJapaneseStyleLabel(typeface.Style);

            styleTagList.Add(new StyleTagItem
            {
                DisplayText = typeface.Style.ToString(),
                JapaneseStyleLabel = japaneseLabel,
                Style = typeface.Style,
                IsSelected = isSelected,
                Typeface = typeface
            });
        }

        StyleTags = styleTagList;
        HasStyles = styleTagList.Count > 0;
    }

    private void UpdateFeatureSummary(TypefaceInfo typeface)
    {
        var features = new List<string>(4);
        if (typeface.HasNerdFonts) features.Add("Nerd Fonts");
        if (typeface.HasLigatures) features.Add("合字");
        if (typeface.HasSlashedZero) features.Add("斜線ゼロ");
        if (typeface.HasIvs) features.Add("IVS");
        Features = features.Count > 0 ? string.Join(" / ", features) : "—";
    }

    private void UpdateVariableFontSummary(TypefaceInfo typeface)
    {
        if (typeface.IsVariableFont && typeface.VariationAxes is { Count: > 0 } axes)
        {
            var summary = string.Join(" / ", axes.Select(axis => axis.SummaryText));
            IsVariableText = $"対応 ({summary})";
        }
        else
        {
            IsVariableText = typeface.IsVariableFont ? "対応" : "非対応";
        }
    }

    private static string GetJapaneseWeightLabel(int weight) => weight switch
    {
        <= 150 => "極細",
        <= 250 => "特細",
        <= 320 => "細字",
        <= 375 => "やや細字",
        <= 450 => "標準",
        <= 550 => "中字",
        <= 650 => "半太字",
        <= 750 => "太字",
        <= 850 => "極太",
        _ => "超極太"
    };

    private static string GetJapaneseStyleLabel(System.Windows.FontStyle style)
    {
        if (style == System.Windows.FontStyles.Normal) return "標準";
        if (style == System.Windows.FontStyles.Italic) return "イタリック";
        if (style == System.Windows.FontStyles.Oblique) return "オブリーク";
        return style.ToString();
    }

    private static int GetStyleSortOrder(System.Windows.FontStyle style)
    {
        if (style == System.Windows.FontStyles.Normal) return 0;
        if (style == System.Windows.FontStyles.Italic) return 1;
        if (style == System.Windows.FontStyles.Oblique) return 2;
        return 3;
    }
}
