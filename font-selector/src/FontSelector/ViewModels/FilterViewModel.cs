using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FontSelector.Models;

namespace FontSelector.ViewModels;

public partial class FilterViewModel : ObservableObject
{
    private readonly MainViewModel _mainViewModel;
    public const string AllOption = "すべて";
    public const string CharSetJis1 = "JIS第1水準 (常用漢字)";
    public const string CharSetJis2 = "JIS第2水準 (人名・旧字)";
    public const string CharSetJis34 = "JIS第3・第4水準 (JIS2004)";
    public const string CharSetEmoji = "絵文字 (Emoji)";

    [ObservableProperty] private string _searchText = string.Empty;

    // Available Options
    [ObservableProperty] private ObservableCollection<string> _availableWeights = [AllOption];
    [ObservableProperty] private ObservableCollection<string> _availableStyles = [AllOption];
    [ObservableProperty] private ObservableCollection<string> _availableStretches = [AllOption];
    [ObservableProperty] private ObservableCollection<string> _availableLanguages = [AllOption];
    [ObservableProperty] private ObservableCollection<string> _availableSpacings = [AllOption];
    [ObservableProperty] private ObservableCollection<string> _availableVendors = [AllOption];
    [ObservableProperty] private ObservableCollection<string> _availableCategories = [AllOption];
    [ObservableProperty] private ObservableCollection<string> _availableFileFormats = [AllOption];
    [ObservableProperty] private ObservableCollection<string> _availableOutlineFormats = [AllOption];
    [ObservableProperty] private ObservableCollection<string> _availableInstallTypes = [AllOption];
    [ObservableProperty] private ObservableCollection<string> _availableCharSets = [AllOption, CharSetJis1, CharSetJis2, CharSetJis34, CharSetEmoji];

    // Selected Options
    [ObservableProperty] private string _selectedWeight = AllOption;
    [ObservableProperty] private string _selectedStyle = AllOption;
    [ObservableProperty] private string _selectedStretch = AllOption;
    [ObservableProperty] private string _selectedLanguage = AllOption;
    [ObservableProperty] private string _selectedSpacing = AllOption;
    [ObservableProperty] private string _selectedVendor = AllOption;
    [ObservableProperty] private string _selectedCategory = AllOption;
    [ObservableProperty] private string _selectedFileFormat = AllOption;
    [ObservableProperty] private string _selectedOutlineFormat = AllOption;
    [ObservableProperty] private string _selectedInstallType = AllOption;
    [ObservableProperty] private string _selectedCharSet = AllOption;

    // Checkbox Filters
    [ObservableProperty] private bool _filterNerdFonts;
    [ObservableProperty] private bool _filterLigatures;
    [ObservableProperty] private bool _filterSlashedZero;
    [ObservableProperty] private bool _filterVariableFont;
    [ObservableProperty] private bool _filterIvs;

    public FilterViewModel(MainViewModel mainViewModel)
    {
        _mainViewModel = mainViewModel;
        PropertyChanged += OnFilterPropertyChanged;
    }

    private void OnFilterPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AvailableWeights) ||
            e.PropertyName == nameof(AvailableStyles) ||
            e.PropertyName == nameof(AvailableStretches) ||
            e.PropertyName == nameof(AvailableLanguages) ||
            e.PropertyName == nameof(AvailableSpacings) ||
            e.PropertyName == nameof(AvailableVendors) ||
            e.PropertyName == nameof(AvailableCategories) ||
            e.PropertyName == nameof(AvailableFileFormats) ||
            e.PropertyName == nameof(AvailableOutlineFormats) ||
            e.PropertyName == nameof(AvailableInstallTypes) ||
            e.PropertyName == nameof(AvailableCharSets))
        {
            return;
        }

        _mainViewModel.ApplyFilters();
    }

    public FontFilterCriteria BuildFilterCriteria()
    {
        return new FontFilterCriteria
        {
            SearchText = SearchText,
            SelectedWeight = SelectedWeight,
            SelectedStyle = SelectedStyle,
            SelectedStretch = SelectedStretch,
            SelectedLanguage = SelectedLanguage,
            SelectedSpacing = SelectedSpacing,
            SelectedVendor = SelectedVendor,
            SelectedCategory = SelectedCategory,
            SelectedFileFormat = SelectedFileFormat,
            SelectedOutlineFormat = SelectedOutlineFormat,
            SelectedInstallType = SelectedInstallType,
            SelectedCharSet = SelectedCharSet,
            FilterNerdFonts = FilterNerdFonts,
            FilterLigatures = FilterLigatures,
            FilterSlashedZero = FilterSlashedZero,
            FilterVariableFont = FilterVariableFont,
            FilterIvs = FilterIvs
        };
    }

    [RelayCommand]
    private void ClearFilters()
    {
        SearchText = string.Empty;
        SelectedWeight = AllOption;
        SelectedStyle = AllOption;
        SelectedStretch = AllOption;
        SelectedLanguage = AllOption;
        SelectedSpacing = AllOption;
        SelectedVendor = AllOption;
        SelectedCategory = AllOption;
        SelectedFileFormat = AllOption;
        SelectedOutlineFormat = AllOption;
        SelectedInstallType = AllOption;
        SelectedCharSet = AllOption;
        FilterNerdFonts = false;
        FilterLigatures = false;
        FilterSlashedZero = false;
        FilterVariableFont = false;
        FilterIvs = false;
    }

    [RelayCommand]
    public void SelectFilterLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language)) return;
        if (string.Equals(SelectedLanguage, language, StringComparison.OrdinalIgnoreCase))
        {
            SelectedLanguage = AllOption;
            return;
        }
        var safeLang = AvailableLanguages.FirstOrDefault(l => l.Equals(language, StringComparison.OrdinalIgnoreCase));
        if (safeLang is not null)
        {
            SelectedLanguage = safeLang;
        }
        else
        {
            AvailableLanguages.Add(language);
            SelectedLanguage = language;
        }
    }

    /// <summary>
    /// プルダウンが空欄（nullまたは空文字）のとき、該当する選択肢（フォント）が存在する場合に
    /// リアルタイムに「すべて」に復帰させます。
    /// </summary>
    /// <returns>いずれかの選択値が「すべて」に復帰した場合は true</returns>
    public bool RestoreEmptySelectionsToAll()
    {
        bool changed = false;
        if (string.IsNullOrWhiteSpace(SelectedLanguage)) { SelectedLanguage = AllOption; changed = true; }
        if (string.IsNullOrWhiteSpace(SelectedWeight)) { SelectedWeight = AllOption; changed = true; }
        if (string.IsNullOrWhiteSpace(SelectedStyle)) { SelectedStyle = AllOption; changed = true; }
        if (string.IsNullOrWhiteSpace(SelectedStretch)) { SelectedStretch = AllOption; changed = true; }
        if (string.IsNullOrWhiteSpace(SelectedSpacing)) { SelectedSpacing = AllOption; changed = true; }
        if (string.IsNullOrWhiteSpace(SelectedVendor)) { SelectedVendor = AllOption; changed = true; }
        if (string.IsNullOrWhiteSpace(SelectedCategory)) { SelectedCategory = AllOption; changed = true; }
        if (string.IsNullOrWhiteSpace(SelectedFileFormat)) { SelectedFileFormat = AllOption; changed = true; }
        if (string.IsNullOrWhiteSpace(SelectedOutlineFormat)) { SelectedOutlineFormat = AllOption; changed = true; }
        if (string.IsNullOrWhiteSpace(SelectedInstallType)) { SelectedInstallType = AllOption; changed = true; }
        if (string.IsNullOrWhiteSpace(SelectedCharSet)) { SelectedCharSet = AllOption; changed = true; }
        return changed;
    }
}
