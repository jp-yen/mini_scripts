using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FontSelector.Models;
using FontSelector.Services;

namespace FontSelector.ViewModels;

public partial class PreviewViewModel : ObservableObject
{
    private readonly MainViewModel _mainViewModel;

    [ObservableProperty] private string _previewText = SampleTextProvider.DefaultPreviewText;
    [ObservableProperty] private string _filteredPreviewText = string.Empty;
    [ObservableProperty] private IReadOnlyList<string> _previewLines = [];

    // Preview Adjustments
    [ObservableProperty] private double _fontSize = 22.0;
    [ObservableProperty] private double _weightMin = 100.0;
    [ObservableProperty] private double _weightMax = 900.0;
    [ObservableProperty] private double _weightValue = 400.0;
    [ObservableProperty] private double _widthMin = 50.0;
    [ObservableProperty] private double _widthMax = 150.0;
    [ObservableProperty] private double _widthScale = 100.0;
    [ObservableProperty] private bool _hasWidthAxis;
    [ObservableProperty] private double _slantMin = -90.0;
    [ObservableProperty] private double _slantMax = 90.0;
    [ObservableProperty] private double _slantAngle = 0.0;
    [ObservableProperty] private bool _hasSlantAxis;
    [ObservableProperty] private bool _isItalic;

    // Variable Fonts Info
    [ObservableProperty] private bool _isSelectedFontVariable;
    [ObservableProperty] private string _variableAxesSummary = string.Empty;

    public bool IsSampleTextMismatched
    {
        get
        {
            if (_mainViewModel.Filters.SelectedCharSet == FilterViewModel.CharSetEmoji)
            {
                var (emojiText, _) = SampleTextProvider.GetEmojiSampleText();
                return !string.Equals(PreviewText, emojiText, StringComparison.Ordinal);
            }

            var currentLang = _mainViewModel.Filters.SelectedLanguage;
            if (string.IsNullOrEmpty(currentLang) || currentLang == FilterViewModel.AllOption)
            {
                currentLang = _mainViewModel.SelectedTypeface?.Languages.FirstOrDefault() ?? "en-US";
            }
            var (defaultText, _) = SampleTextProvider.GetSampleTextForLanguage(currentLang);
            return !string.Equals(PreviewText, defaultText, StringComparison.Ordinal);
        }
    }

    public PreviewViewModel(MainViewModel mainViewModel)
    {
        _mainViewModel = mainViewModel;
        PropertyChanged += OnPreviewPropertyChanged;
        _mainViewModel.PropertyChanged += OnMainViewModelPropertyChanged;
        _mainViewModel.Filters.PropertyChanged += OnFilterPropertyChanged;
        UpdatePreviewLines();
    }

    private void OnPreviewPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PreviewText))
        {
            UpdatePreviewLines();
            OnPropertyChanged(nameof(IsSampleTextMismatched));
        }
    }

    private void OnMainViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedTypeface))
        {
            UpdatePreviewSettingsForTypeface(_mainViewModel.SelectedTypeface);
            UpdatePreviewLines();
            OnPropertyChanged(nameof(IsSampleTextMismatched));
        }
        else if (e.PropertyName == nameof(MainViewModel.IsLoading))
        {
            if (!_mainViewModel.IsLoading)
            {
                UpdatePreviewLines();
            }
        }
    }

    private void OnFilterPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FilterViewModel.SelectedLanguage) ||
            e.PropertyName == nameof(FilterViewModel.SelectedCharSet))
        {
            OnPropertyChanged(nameof(IsSampleTextMismatched));
        }
    }

    public void UpdatePreviewLines()
    {
        if (string.IsNullOrEmpty(PreviewText))
        {
            PreviewLines = [];
            FilteredPreviewText = string.Empty;
            return;
        }

        var typeface = _mainViewModel.SelectedTypeface;
        if (typeface is null)
        {
            var lines = PreviewText.Split(["\r\n", "\n"], StringSplitOptions.None);
            PreviewLines = lines;
            FilteredPreviewText = PreviewText;
            return;
        }

        var filtered = FilterTextWithTofu(PreviewText, typeface);
        FilteredPreviewText = filtered;
        PreviewLines = filtered.Split(["\r\n", "\n"], StringSplitOptions.None);
    }

    private static string FilterTextWithTofu(string text, TypefaceInfo typeface)
    {
        if (string.IsNullOrEmpty(text)) return text;

        var sb = new System.Text.StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            int codePoint;
            int charCount = 1;
            if (char.IsSurrogatePair(text, i))
            {
                codePoint = char.ConvertToUtf32(text, i);
                charCount = 2;
            }
            else
            {
                codePoint = text[i];
            }

            // 改行・タブ・半角スペースなどの制御・基本空白文字はそのまま維持
            if (codePoint == '\r' || codePoint == '\n' || codePoint == '\t' || codePoint == ' ')
            {
                sb.Append(text.Substring(i, charCount));
            }
            else if (typeface.HasGlyph(codePoint))
            {
                sb.Append(text.Substring(i, charCount));
            }
            else
            {
                // フォントに含まれていない文字はフォールバックさせず □ (U+25A1) で表示
                sb.Append('□');
            }

            if (charCount == 2)
            {
                i++;
            }
        }

        return sb.ToString();
    }

    private void UpdatePreviewSettingsForTypeface(TypefaceInfo? typeface)
    {
        if (typeface is null)
        {
            ResetAdjustments();
            IsSelectedFontVariable = false;
            VariableAxesSummary = string.Empty;
            return;
        }

        IsSelectedFontVariable = typeface.IsVariableFont;
        
        var wghtAxis = typeface.VariationAxes?.FirstOrDefault(a => a.Tag == "wght");
        if (wghtAxis is not null)
        {
            WeightMin = wghtAxis.MinValue;
            WeightMax = wghtAxis.MaxValue;
            WeightValue = Math.Clamp(typeface.Weight.ToOpenTypeWeight(), WeightMin, WeightMax);
        }
        else
        {
            WeightMin = 100.0;
            WeightMax = 900.0;
            WeightValue = typeface.Weight.ToOpenTypeWeight();
        }

        var slntAxis = typeface.VariationAxes?.FirstOrDefault(a => a.Tag == "slnt");
        HasSlantAxis = slntAxis is not null;
        if (slntAxis is not null)
        {
            SlantMin = slntAxis.MinValue;
            SlantMax = slntAxis.MaxValue;
            SlantAngle = Math.Clamp(0.0, SlantMin, SlantMax);
        }
        else
        {
            SlantMin = -90.0;
            SlantMax = 90.0;
            SlantAngle = 0.0;
        }

        var wdthAxis = typeface.VariationAxes?.FirstOrDefault(a => a.Tag == "wdth");
        HasWidthAxis = wdthAxis is not null;
        if (wdthAxis is not null)
        {
            WidthMin = wdthAxis.MinValue;
            WidthMax = wdthAxis.MaxValue;
            WidthScale = Math.Clamp(100.0, WidthMin, WidthMax);
        }
        else
        {
            WidthMin = 50.0;
            WidthMax = 150.0;
            WidthScale = 100.0;
        }

        IsItalic = typeface.Style == FontStyles.Italic || typeface.Style == FontStyles.Oblique;

        if (typeface.VariationAxes is not null && typeface.VariationAxes.Count > 0)
        {
            var tags = typeface.VariationAxes.Select(a => a.Tag).ToList();
            VariableAxesSummary = $"可変軸: {string.Join(", ", tags)}";
        }
        else
        {
            VariableAxesSummary = string.Empty;
        }
    }

    [RelayCommand]
    public void ResetAdjustments()
    {
        FontSize = 22.0;
        if (_mainViewModel.SelectedTypeface is not null)
        {
            UpdatePreviewSettingsForTypeface(_mainViewModel.SelectedTypeface);
        }
        else
        {
            WeightValue = 400.0;
            WidthMin = 50.0;
            WidthMax = 150.0;
            WidthScale = 100.0;
            SlantAngle = 0.0;
            IsItalic = false;
        }
    }

    [RelayCommand]
    private void ApplyEmojiSampleText()
    {
        var result = SampleTextProvider.GetEmojiSampleText();
        PreviewText = result.text;
    }

    [RelayCommand]
    private void ApplyNerdFontsSampleText()
    {
        var result = SampleTextProvider.GetNerdFontsSampleText();
        PreviewText = result.text;
    }

    [RelayCommand]
    private void ApplyLanguageSampleText()
    {
        if (_mainViewModel.Filters.SelectedCharSet == FilterViewModel.CharSetEmoji)
        {
            ApplyEmojiSampleText();
            return;
        }

        var currentLang = _mainViewModel.Filters.SelectedLanguage;
        if (string.IsNullOrEmpty(currentLang) || currentLang == FilterViewModel.AllOption)
        {
            currentLang = _mainViewModel.SelectedTypeface?.Languages.FirstOrDefault() ?? "en-US";
        }
        var result = SampleTextProvider.GetSampleTextForLanguage(currentLang);
        PreviewText = result.text;
    }
}
