using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using FontSelector.Models;
using FontSelector.Services;

namespace FontSelector.ViewModels;

/// <summary>
/// Main ViewModel managing font listing, filtering, preview, and theme state.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly IFontService _fontService;
    private readonly IThemeService _themeService;
    
    public IReadOnlyList<FontFamilyInfo> AllFonts { get; private set; } = [];

    public FilterViewModel Filters { get; }
    public PreviewViewModel Preview { get; }
    public FontInfoViewModel FontInfo { get; }

    // ═══════════════════════════════════════════════════════
    // Loading state
    // ═══════════════════════════════════════════════════════

    [ObservableProperty] private bool _isLoading = true;
    [ObservableProperty] private double _loadingProgressPercent;
    [ObservableProperty] private double _loadingListProgressPercent;
    [ObservableProperty] private double _loadingDetailProgressPercent;
    [ObservableProperty] private string _loadingListStatus = "準備中...";
    [ObservableProperty] private string _loadingDetailStatus = "待機中...";
    [ObservableProperty] private string _loadingStatusText = "システムフォントを検索中...";
    [ObservableProperty] private string _loadingFontName = string.Empty;

    // ═══════════════════════════════════════════════════════
    // Status bar & Theme core properties
    // ═══════════════════════════════════════════════════════

    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private int _totalFontCount;
    [ObservableProperty] private int _filteredFontCount;
    [ObservableProperty] private int _totalFamilyCount;
    [ObservableProperty] private int _totalTypefaceCount;
    [ObservableProperty] private int _filteredFamilyCount;
    [ObservableProperty] private string _currentTheme = "tal7aouy";
    [ObservableProperty] private bool _isDarkTheme = true;

    // ═══════════════════════════════════════════════════════
    // Filtered results
    // ═══════════════════════════════════════════════════════

    [ObservableProperty] private ObservableCollection<FontFamilyInfo> _filteredFamilies = [];
    [ObservableProperty] private FontFamilyInfo? _selectedFamily;
    [ObservableProperty] private ObservableCollection<TypefaceInfo> _filteredTypefaces = [];
    [ObservableProperty] private TypefaceInfo? _selectedTypeface;

    public MainViewModel(IFontService fontService, IThemeService themeService)
    {
        _fontService = fontService;
        _themeService = themeService;

        CurrentTheme = _themeService.CurrentTheme;
        IsDarkTheme = _themeService.IsDarkTheme;
        _themeService.ThemeChanged += theme =>
        {
            CurrentTheme = theme;
            IsDarkTheme = _themeService.IsDarkTheme;
        };

        Filters = new FilterViewModel(this);
        Preview = new PreviewViewModel(this);
        FontInfo = new FontInfoViewModel(this);
    }

    public string GetPreviewTextColor() => _themeService.GetPreviewTextColor(CurrentTheme);

    partial void OnSelectedFamilyChanged(FontFamilyInfo? value)
    {
        if (value is null)
        {
            SelectedTypeface = null;
            return;
        }

        UpdateTypefacesFilterMatch(value);
        SelectedTypeface = value.FindBestMatchingTypeface(Filters.BuildFilterCriteria());
    }

    partial void OnSelectedTypefaceChanged(TypefaceInfo? value)
    {
        FontInfo.UpdateFromTypeface(value);
    }

    public async Task InitializeAsync()
    {
        IsLoading = true;
        LoadingProgressPercent = 0;
        LoadingListProgressPercent = 0;
        LoadingDetailProgressPercent = 0;
        LoadingListStatus = "検索中...";
        LoadingDetailStatus = "待機中...";

        double phase1Percent = 0;
        double phase2Percent = 0;

        var progress = new Progress<(int current, int total, string fontName, int phase)>(p =>
        {
            if (p.phase == 1)
            {
                phase1Percent = p.total > 0 ? (double)p.current / p.total * 30.0 : 0;
                var listPercent = p.total > 0 ? (double)p.current / p.total * 100.0 : 0;
                if (listPercent > LoadingListProgressPercent)
                {
                    LoadingListProgressPercent = Math.Min(100.0, listPercent);
                }

                LoadingListStatus = p.current >= p.total
                    ? $"完了 {p.total:N0} ファミリー"
                    : $"{p.current:N0} / {p.total:N0}  {p.fontName}";
                LoadingStatusText = $"リスト作成中 {p.current:N0}/{p.total:N0}";
            }
            else
            {
                phase2Percent = p.total > 0 ? Math.Min(70.0, (double)p.current / p.total * 70.0) : 0;
                var detailPercent = p.total > 0 ? (double)p.current / p.total * 100.0 : 0;
                if (detailPercent > LoadingDetailProgressPercent)
                {
                    LoadingDetailProgressPercent = Math.Min(100.0, detailPercent);
                }

                LoadingDetailStatus = p.total > 0 && p.current >= p.total
                    ? $"完了 {p.total:N0} 書体"
                    : $"{p.current:N0} / {p.total:N0}  {p.fontName}";
                LoadingStatusText = $"フォント解析中 {p.current:N0}/{p.total:N0}";
                LoadingFontName = p.fontName;
            }

            var combinedPercent = Math.Min(99.0, phase1Percent + phase2Percent);
            if (combinedPercent > LoadingProgressPercent)
            {
                LoadingProgressPercent = combinedPercent;
            }
        });

        try
        {
            AllFonts = await _fontService.GetAllFontsAsync(progress);
            LoadingProgressPercent = 100.0;
            LoadingListProgressPercent = 100.0;
            LoadingDetailProgressPercent = 100.0;
            TotalFamilyCount = AllFonts.Count;
            TotalTypefaceCount = AllFonts.Sum(f => f.Typefaces.Count);
            TotalFontCount = TotalTypefaceCount;
            LoadingListStatus = $"完了 {TotalFamilyCount:N0} ファミリー";
            LoadingDetailStatus = $"完了 {TotalTypefaceCount:N0} 書体";
            ApplyFilters();
        }
        catch (Exception ex)
        {
            LoadingStatusText = $"エラーが発生しました: {ex.Message}";
            LoadingDetailStatus = $"エラー: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    // ═══════════════════════════════════════════════════════
    // フィルタリング処理
    // ═══════════════════════════════════════════════════════

    private void UpdateTypefacesFilterMatch(FontFamilyInfo? family, FontFilterCriteria? criteria = null)
    {
        if (family is null) return;
        criteria ??= Filters.BuildFilterCriteria();
        foreach (var tf in family.Typefaces)
        {
            tf.IsFilterMatched = criteria.MatchesTypeface(tf);
        }
    }

    private bool _isUpdatingFilters;

    public void ApplyFilters()
    {
        if (_isUpdatingFilters || AllFonts.Count == 0) return;
        _isUpdatingFilters = true;
        try
        {
            var criteria = Filters.BuildFilterCriteria();

            var baseList = criteria.ApplyBaseQuery(AllFonts).ToList();

            // プルダウンが空欄の時に検索文字列を変更してその項目に該当するものが出てきたとき、
            // リアルタイムに「すべて」に復帰させる
            if (baseList.Count > 0)
            {
                var restored = Filters.RestoreEmptySelectionsToAll();
                if (restored)
                {
                    criteria = Filters.BuildFilterCriteria();
                }
            }

            var filtered = baseList.Where(fam => criteria.MatchesFamily(fam))
                .OrderBy(fam => fam.FamilyName)
                .ToList();

            var previousFamily = SelectedFamily;
            if (!FilteredFamilies.SequenceEqual(filtered))
            {
                FilteredFamilies = new ObservableCollection<FontFamilyInfo>(filtered);
            }
            FilteredFamilyCount = filtered.Count;
            FilteredFontCount = filtered.Sum(f => f.Typefaces.Count);
            FilteredTypefaces = new ObservableCollection<TypefaceInfo>(filtered.Select(f => f.DefaultTypeface));
            StatusText = $"該当: {FilteredFamilyCount} ファミリー ({FilteredFontCount} 書体) / 全 {TotalFamilyCount} ファミリー ({TotalTypefaceCount} 書体)";

            FontFamilyInfo? nextFamily = null;
            if (previousFamily is not null)
            {
                nextFamily = filtered.FirstOrDefault(fam => fam.FamilyName == previousFamily.FamilyName);
            }
            var newFamily = nextFamily ?? filtered.FirstOrDefault();
            if (ReferenceEquals(SelectedFamily, newFamily))
            {
                OnPropertyChanged(nameof(SelectedFamily));
            }
            else
            {
                SelectedFamily = newFamily;
            }

            UpdateTypefacesFilterMatch(SelectedFamily, criteria);

            if (SelectedFamily is not null)
            {
                if (SelectedTypeface is null ||
                    !SelectedTypeface.IsFilterMatched ||
                    !SelectedFamily.Typefaces.Contains(SelectedTypeface))
                {
                    SelectedTypeface = SelectedFamily.FindBestMatchingTypeface(criteria);
                }
            }
            else
            {
                SelectedTypeface = null;
            }

            UpdateDropdownWeights(baseList, criteria);
            UpdateDropdownStyles(baseList, criteria);

            UpdateDropdown(baseList, criteria, FontFilterType.Stretch,
                fam => fam.Typefaces.Where(tf => criteria.MatchesTypeface(tf, FontFilterType.Stretch)).Select(tf => tf.Stretch.ToString()),
                Filters.AvailableStretches,
                v => Filters.AvailableStretches = v);

            UpdateDropdown(baseList, criteria, FontFilterType.Language,
                fam => fam.Typefaces.Where(tf => criteria.MatchesTypeface(tf, FontFilterType.Language)).SelectMany(tf => tf.Languages),
                Filters.AvailableLanguages,
                v => Filters.AvailableLanguages = v);

            UpdateDropdown(baseList, criteria, FontFilterType.Spacing,
                fam => fam.Typefaces.Where(tf => criteria.MatchesTypeface(tf, FontFilterType.Spacing)).Select(tf => tf.Spacing),
                Filters.AvailableSpacings,
                v => Filters.AvailableSpacings = v, skipEmpty: true);

            UpdateDropdownSingle(baseList, criteria, FontFilterType.Vendor, fam => fam.Vendor, Filters.AvailableVendors, v => Filters.AvailableVendors = v, skipEmpty: true);
            UpdateDropdownSingle(baseList, criteria, FontFilterType.Category, fam => fam.Category, Filters.AvailableCategories, v => Filters.AvailableCategories = v, skipEmpty: true);

            UpdateDropdown(baseList, criteria, FontFilterType.FileFormat,
                fam => fam.Typefaces.Where(tf => criteria.MatchesTypeface(tf, FontFilterType.FileFormat)).Select(tf => tf.FileFormat),
                Filters.AvailableFileFormats,
                v => Filters.AvailableFileFormats = v, skipEmpty: true);

            UpdateDropdown(baseList, criteria, FontFilterType.OutlineFormat,
                fam => fam.Typefaces.Where(tf => criteria.MatchesTypeface(tf, FontFilterType.OutlineFormat)).Select(tf => tf.OutlineFormat),
                Filters.AvailableOutlineFormats,
                v => Filters.AvailableOutlineFormats = v, skipEmpty: true);

            UpdateDropdown(baseList, criteria, FontFilterType.InstallType,
                fam => fam.Typefaces.Where(tf => criteria.MatchesTypeface(tf, FontFilterType.InstallType)).Select(tf => tf.InstallType),
                Filters.AvailableInstallTypes,
                v => Filters.AvailableInstallTypes = v, skipEmpty: true);
        }
        finally
        {
            _isUpdatingFilters = false;
        }
    }

    private void UpdateDropdownWeights(List<FontFamilyInfo> baseList, FontFilterCriteria criteria)
    {
        var availableWeights = baseList.Where(fam => criteria.MatchesFamily(fam, FontFilterType.Weight))
            .SelectMany(fam => fam.Typefaces.Where(tf => criteria.MatchesTypeface(tf, FontFilterType.Weight)))
            .GroupBy(tf => tf.WeightDisplay)
            .OrderBy(g => g.First().Weight.ToOpenTypeWeight())
            .Select(g => g.Key)
            .ToList();
        
        var targetList = new List<string> { FilterViewModel.AllOption };
        targetList.AddRange(availableWeights);

        if (!Filters.AvailableWeights.SequenceEqual(targetList))
        {
            Filters.AvailableWeights = new ObservableCollection<string>(targetList);
        }
    }

    private void UpdateDropdownStyles(List<FontFamilyInfo> baseList, FontFilterCriteria criteria)
    {
        var availableStyles = baseList.Where(fam => criteria.MatchesFamily(fam, FontFilterType.Style))
            .SelectMany(fam => fam.Typefaces.Where(tf => criteria.MatchesTypeface(tf, FontFilterType.Style)))
            .Select(tf => tf.Style.ToString())
            .Distinct()
            .OrderBy(s => s switch
            {
                "Normal" => 0,
                "Italic" => 1,
                "Oblique" => 2,
                _ => 3
            })
            .ToList();

        var targetList = new List<string> { FilterViewModel.AllOption };
        targetList.AddRange(availableStyles);

        if (!Filters.AvailableStyles.SequenceEqual(targetList))
        {
            Filters.AvailableStyles = new ObservableCollection<string>(targetList);
        }
    }

    private static void UpdateDropdown(
        IEnumerable<FontFamilyInfo> baseList,
        FontFilterCriteria criteria,
        FontFilterType filterType,
        Func<FontFamilyInfo, IEnumerable<string>> selector,
        ObservableCollection<string> current,
        Action<ObservableCollection<string>> setter,
        bool skipEmpty = false)
    {
        var available = baseList.Where(fam => criteria.MatchesFamily(fam, filterType))
            .SelectMany(selector)
            .Where(v => !skipEmpty || (!string.IsNullOrEmpty(v) && v != "不明"))
            .Distinct()
            .OrderBy(v => v)
            .ToList();

        var targetList = new List<string> { FilterViewModel.AllOption };
        targetList.AddRange(available);

        if (!current.SequenceEqual(targetList))
        {
            setter(new ObservableCollection<string>(targetList));
        }
    }

    private static void UpdateDropdownSingle(
        IEnumerable<FontFamilyInfo> baseList,
        FontFilterCriteria criteria,
        FontFilterType filterType,
        Func<FontFamilyInfo, string> selector,
        ObservableCollection<string> current,
        Action<ObservableCollection<string>> setter,
        bool skipEmpty = false)
    {
        var available = baseList.Where(fam => criteria.MatchesFamily(fam, filterType))
            .Select(selector)
            .Where(v => !skipEmpty || (!string.IsNullOrEmpty(v) && v != "不明"))
            .Distinct()
            .OrderBy(v => v)
            .ToList();

        var targetList = new List<string> { FilterViewModel.AllOption };
        targetList.AddRange(available);

        if (!current.SequenceEqual(targetList))
        {
            setter(new ObservableCollection<string>(targetList));
        }
    }
}
