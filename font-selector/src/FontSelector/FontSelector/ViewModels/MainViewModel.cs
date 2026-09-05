using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FontSelector.Models;
using FontSelector.Services;

namespace FontSelector.ViewModels;

/// <summary>
/// Main ViewModel managing font listing, filtering, preview, and theme state.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly IFontService _fontService;
    private IReadOnlyList<FontFamilyInfo> _allFonts = [];

    public const string AllOption = "すべて";
    public const string CharSetJis1 = "JIS第1水準 (常用漢字)";
    public const string CharSetJis2 = "JIS第2水準 (人名・旧字)";
    public const string CharSetJis34 = "JIS第3・第4水準 (JIS2004)";
    public const string CharSetEmoji = "絵文字 (Emoji)";

    // ═══════════════════════════════════════════════════════
    // Filter dropdown available values (cascading)
    // ═══════════════════════════════════════════════════════

    [ObservableProperty] private ObservableCollection<string> _availableWeights = [];
    [ObservableProperty] private ObservableCollection<string> _availableStyles = [];
    [ObservableProperty] private ObservableCollection<string> _availableStretches = [];
    [ObservableProperty] private ObservableCollection<string> _availableLanguages = [];
    [ObservableProperty] private ObservableCollection<string> _availableSpacings = [];
    [ObservableProperty] private ObservableCollection<string> _availableVendors = [];
    [ObservableProperty] private ObservableCollection<string> _availableCategories = [];
    [ObservableProperty] private ObservableCollection<string> _availableFileFormats = [];
    [ObservableProperty] private ObservableCollection<string> _availableOutlineFormats = [];
    [ObservableProperty] private ObservableCollection<string> _availableInstallTypes = [];
    [ObservableProperty] private ObservableCollection<string> _availableCharSets = [AllOption, CharSetJis1, CharSetJis2, CharSetJis34, CharSetEmoji];

    // ═══════════════════════════════════════════════════════
    // Filter selections — dropdowns
    // ═══════════════════════════════════════════════════════

    [ObservableProperty] private string _searchText = string.Empty;
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

    // ═══════════════════════════════════════════════════════
    // Filter selections — checkboxes
    // ═══════════════════════════════════════════════════════

    [ObservableProperty] private bool _filterNerdFonts;
    [ObservableProperty] private bool _filterLigatures;
    [ObservableProperty] private bool _filterSlashedZero;
    [ObservableProperty] private bool _filterVariableFont;
    [ObservableProperty] private bool _filterIvs;

    // ═══════════════════════════════════════════════════════
    // Filtered results
    // ═══════════════════════════════════════════════════════

    [ObservableProperty] private ObservableCollection<FontFamilyInfo> _filteredFamilies = [];
    [ObservableProperty] private FontFamilyInfo? _selectedFamily;
    [ObservableProperty] private ObservableCollection<TypefaceInfo> _filteredTypefaces = [];
    [ObservableProperty] private TypefaceInfo? _selectedTypeface;

    partial void OnSelectedFamilyChanged(FontFamilyInfo? value)
    {
        if (value is null)
        {
            SelectedTypeface = null;
            return;
        }

        UpdateTypefacesFilterMatch(value);

        // フィルター条件（言語・文字セット・太さ・スタイル・ストレッチ等）に最も適合する書体を選択
        var best = value.FindBestMatchingTypeface(
            SelectedWeight, SelectedStyle, SelectedStretch,
            SelectedLanguage, SelectedCharSet,
            FilterNerdFonts, FilterLigatures, FilterSlashedZero,
            FilterVariableFont, FilterIvs);
        SelectedTypeface = best;
    }

    // ═══════════════════════════════════════════════════════
    // Preview state
    // ═══════════════════════════════════════════════════════

    [ObservableProperty] private double _previewFontSize = 24.0;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewLines))]
    [NotifyPropertyChangedFor(nameof(FilteredPreviewText))]
    [NotifyPropertyChangedFor(nameof(IsSampleTextMismatched))]
    private string _previewText = SampleTextProvider.DefaultPreviewText;

    /// <summary>
    /// 直前に「言語の文例」ボタンによって設定された言語名（手入力された場合は null）
    /// </summary>
    private string? _appliedSampleLanguage;

    /// <summary>
    /// 文例が現在の言語選択（またはフォントの代表言語）と不一致かどうか
    /// （ユーザーが手入力した場合は false）
    /// </summary>
    public bool IsSampleTextMismatched
    {
        get
        {
            if (string.IsNullOrEmpty(_appliedSampleLanguage)) return false;

            var currentTargetLang = GetCurrentTargetLanguage();
            return !string.Equals(_appliedSampleLanguage, currentTargetLang, StringComparison.OrdinalIgnoreCase);
        }
    }

    private string GetCurrentTargetLanguage()
    {
        if (SelectedCharSet == CharSetEmoji)
        {
            return "絵文字";
        }

        if (!string.IsNullOrEmpty(SelectedLanguage) && SelectedLanguage != AllOption)
        {
            return SelectedLanguage;
        }

        return SelectedTypeface?.Languages.FirstOrDefault() ?? "日本語";
    }

    public string FilteredPreviewText => FilterTextForFallback(PreviewText);

    public IReadOnlyList<string> PreviewLines =>
        FilteredPreviewText.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);

    private string FilterTextForFallback(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        if (SelectedTypeface is null)
            return text;

        var sb = new StringBuilder(text.Length);
        foreach (var rune in text.EnumerateRunes())
        {
            var cp = rune.Value;

            // 空白・改行・制御文字
            if (Rune.IsControl(rune) || Rune.IsWhiteSpace(rune))
            {
                sb.Append(rune);
                continue;
            }

            // 異体字セレクタ (VS1〜VS16: U+FE00〜U+FE0F, U+E0100〜U+E01EF) や ZWJ (U+200D), ZWNJ (U+200C)
            // 直前の絵文字のスタイル指定や結合を行う修飾子の文字化け・分断を防止
            if ((cp >= 0xFE00 && cp <= 0xFE0F) ||
                (cp >= 0xE0100 && cp <= 0xE01EF) ||
                cp == 0x200D || cp == 0x200C)
            {
                sb.Append(rune);
                continue;
            }

            // 言語文字・絵文字・シンボルを問わず、選択フォントに収録されているかを厳密判定
            // 未収録文字はすべて「□」で表示（フォント選択ツールとして異なるフォントが混ざるのを確実に防止）
            if (SelectedTypeface.HasGlyph(cp))
            {
                sb.Append(rune);
            }
            else
            {
                sb.Append('□');
            }
        }
        return sb.ToString();
    }

    // ── バリアブルフォント / プレビュー微調整 ──
    [ObservableProperty] private double _previewWeightValue = 400.0;
    [ObservableProperty] private double _previewWeightMin = 100.0;
    [ObservableProperty] private double _previewWeightMax = 900.0;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewScaleX))]
    private double _previewWidthScale = 100.0; // 水平拡大縮小（%）
    public double PreviewScaleX => PreviewWidthScale / 100.0;
    [ObservableProperty] private double _previewSlantAngle = 0.0;   // 傾斜角度（度）
    [ObservableProperty] private double _previewSlantMin = -20.0;
    [ObservableProperty] private double _previewSlantMax = 20.0;
    [ObservableProperty] private bool _hasSlantAxis;
    [ObservableProperty] private bool _isSelectedFontVariable;
    [ObservableProperty] private string _variableAxesSummary = string.Empty;
    [ObservableProperty] private ObservableCollection<VariationAxis> _currentVariationAxes = [];
    [ObservableProperty] private bool _previewIsItalic;
    [ObservableProperty] private bool _hasItalicAxis;

    // ═══════════════════════════════════════════════════════
    // Info panel
    // ═══════════════════════════════════════════════════════

    [ObservableProperty] private string _infoFamily = string.Empty;
    [ObservableProperty] private string _infoAllFamilyNames = string.Empty;
    [ObservableProperty] private string _infoWeight = string.Empty;
    [ObservableProperty] private string _infoStyle = string.Empty;
    [ObservableProperty] private string _infoStretch = string.Empty;
    [ObservableProperty] private string _infoTypefaceCount = string.Empty;
    [ObservableProperty] private string _infoTypefacesTooltip = string.Empty;
    [ObservableProperty] private string _infoVendor = string.Empty;
    [ObservableProperty] private string _infoSpacing = string.Empty;
    [ObservableProperty] private string _infoLanguages = string.Empty;
    [ObservableProperty] private ObservableCollection<LanguageTagItem> _infoLanguageTags = [];
    [ObservableProperty] private bool _hasLanguages;
    [ObservableProperty] private string _infoCategory = string.Empty;
    [ObservableProperty] private string _infoFileFormat = string.Empty;
    [ObservableProperty] private string _infoInstallType = string.Empty;
    [ObservableProperty] private string _infoFeatures = string.Empty;
    [ObservableProperty] private string _infoLicense = string.Empty;
    [ObservableProperty] private string _infoVersion = string.Empty;
    [ObservableProperty] private string _infoOutlineFormat = string.Empty;
    [ObservableProperty] private string _infoFilePath = string.Empty;
    [ObservableProperty] private string _infoFileName = string.Empty;
    [ObservableProperty] private bool _infoIsVariable;
    [ObservableProperty] private string _infoIsVariableText = string.Empty;
    [ObservableProperty] private bool _infoHasNerdFonts;

    // ═══════════════════════════════════════════════════════
    // Loading state
    // ═══════════════════════════════════════════════════════

    [ObservableProperty] private bool _isLoading = true;
    [ObservableProperty] private double _loadingProgressPercent;
    [ObservableProperty] private string _loadingStatusText = "システムフォントを検索中...";
    [ObservableProperty] private string _loadingFontName = string.Empty;

    // ═══════════════════════════════════════════════════════
    // Status bar & Theme
    // ═══════════════════════════════════════════════════════

    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private int _totalFontCount;
    [ObservableProperty] private int _filteredFontCount;
    [ObservableProperty] private int _totalFamilyCount;
    [ObservableProperty] private int _totalTypefaceCount;
    [ObservableProperty] private int _filteredFamilyCount;
    [ObservableProperty] private string _currentTheme = "tal7aouy"; // "tal7aouy", "spinel", "dark", "light"
    [ObservableProperty] private bool _isDarkTheme = true;

    public MainViewModel(IFontService fontService)
    {
        _fontService = fontService;
    }

    public async Task InitializeAsync()
    {
        IsLoading = true;
        LoadingStatusText = "システムフォントを検索中...";
        LoadingProgressPercent = 0;

        var progress = new Progress<(int current, int total, string fontName, int phase)>(p =>
        {
            LoadingProgressPercent = (double)p.current / p.total * 100.0;
            LoadingStatusText = $"フォントを解析中 フェーズ {p.phase}/2 ({p.current}/{p.total})";
            LoadingFontName = p.fontName;
        });

        try
        {
            _allFonts = await _fontService.GetAllFontsAsync(progress);
            TotalFamilyCount = _allFonts.Count;
            TotalTypefaceCount = _allFonts.Sum(f => f.Typefaces.Count);
            TotalFontCount = TotalTypefaceCount;
            ApplyFilters();
        }
        catch (Exception ex)
        {
            LoadingStatusText = $"エラーが発生しました: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    // ═══════════════════════════════════════════════════════
    // Filter change handlers
    // ═══════════════════════════════════════════════════════

    partial void OnSearchTextChanged(string value) => ApplyFilters();
    partial void OnSelectedWeightChanged(string value) { if (value is null) SelectedWeight = AllOption; else ApplyFilters(); }
    partial void OnSelectedStyleChanged(string value) { if (value is null) SelectedStyle = AllOption; else ApplyFilters(); }
    partial void OnSelectedStretchChanged(string value) { if (value is null) SelectedStretch = AllOption; else ApplyFilters(); }
    partial void OnSelectedLanguageChanged(string value)
    {
        if (value is null)
        {
            SelectedLanguage = AllOption;
        }
        else
        {
            ApplyFilters();
            UpdateInfoLanguageTags();
            OnPropertyChanged(nameof(IsSampleTextMismatched));
        }
    }
    partial void OnSelectedSpacingChanged(string value) { if (value is null) SelectedSpacing = AllOption; else ApplyFilters(); }
    partial void OnSelectedVendorChanged(string value) { if (value is null) SelectedVendor = AllOption; else ApplyFilters(); }
    partial void OnSelectedCategoryChanged(string value) { if (value is null) SelectedCategory = AllOption; else ApplyFilters(); }
    partial void OnSelectedFileFormatChanged(string value) { if (value is null) SelectedFileFormat = AllOption; else ApplyFilters(); }
    partial void OnSelectedOutlineFormatChanged(string value) { if (value is null) SelectedOutlineFormat = AllOption; else ApplyFilters(); }
    partial void OnSelectedInstallTypeChanged(string value) { if (value is null) SelectedInstallType = AllOption; else ApplyFilters(); }
    partial void OnSelectedCharSetChanged(string value) { if (value is null) SelectedCharSet = AllOption; else { ApplyFilters(); OnPropertyChanged(nameof(IsSampleTextMismatched)); } }
    partial void OnFilterNerdFontsChanged(bool value) => ApplyFilters();
    partial void OnFilterLigaturesChanged(bool value) => ApplyFilters();
    partial void OnFilterSlashedZeroChanged(bool value) => ApplyFilters();
    partial void OnFilterVariableFontChanged(bool value) => ApplyFilters();
    partial void OnFilterIvsChanged(bool value) => ApplyFilters();

    partial void OnSelectedTypefaceChanged(TypefaceInfo? value)
    {
        if (value is null)
        {
            InfoFamily = InfoAllFamilyNames = InfoWeight = InfoStyle = InfoStretch = string.Empty;
            InfoTypefaceCount = InfoTypefacesTooltip = InfoVendor = InfoSpacing = InfoLanguages = string.Empty;
            InfoLanguageTags.Clear();
            HasLanguages = false;
            InfoCategory = InfoFileFormat = InfoInstallType = InfoFeatures = string.Empty;
            InfoLicense = InfoVersion = InfoOutlineFormat = InfoFilePath = InfoFileName = string.Empty;
            InfoIsVariable = false;
            InfoIsVariableText = string.Empty;
            InfoHasNerdFonts = false;
            IsSelectedFontVariable = false;
            VariableAxesSummary = string.Empty;
            CurrentVariationAxes.Clear();
            HasItalicAxis = false;
            OnPropertyChanged(nameof(PreviewLines));
            OnPropertyChanged(nameof(FilteredPreviewText));
            OnPropertyChanged(nameof(IsSampleTextMismatched));
            return;
        }

        OnPropertyChanged(nameof(PreviewLines));
        OnPropertyChanged(nameof(FilteredPreviewText));
        OnPropertyChanged(nameof(IsSampleTextMismatched));

        InfoFamily = value.FamilyName;
        InfoAllFamilyNames = string.Join(" / ", value.AllFamilyNames);
        InfoWeight = value.WeightDisplay;
        InfoStyle = value.Style.ToString();
        InfoStretch = value.Stretch.ToString();
        InfoVendor = string.IsNullOrEmpty(value.Vendor) ? "—" : value.Vendor;
        InfoSpacing = value.Spacing;
        InfoLanguages = value.Languages.Count > 0 ? string.Join(", ", value.Languages) : "—";
        UpdateInfoLanguageTags();
        InfoCategory = value.Category;
        InfoFileFormat = value.FileFormat;
        InfoInstallType = value.InstallType;
        InfoIsVariable = value.IsVariableFont;
        InfoHasNerdFonts = value.HasNerdFonts;
        InfoVersion = value.Version;
        InfoOutlineFormat = value.OutlineFormat;
        InfoFilePath = value.FilePath;
        InfoFileName = !string.IsNullOrEmpty(value.FilePath)
            ? System.IO.Path.GetFileName(value.FilePath)
            : "—";

        // バリアブルフォント軸情報の反映
        IsSelectedFontVariable = value.IsVariableFont;
        if (value.IsVariableFont && value.VariationAxes.Count > 0)
        {
            CurrentVariationAxes = new ObservableCollection<VariationAxis>(value.VariationAxes);
            VariableAxesSummary = string.Join(" / ", value.VariationAxes.Select(a => a.SummaryText));
            InfoIsVariableText = $"対応 ({VariableAxesSummary})";

            var wght = value.VariationAxes.FirstOrDefault(a => a.Tag == "wght");
            var targetWeight = (double)value.Weight.ToOpenTypeWeight();
            if (wght is not null)
            {
                PreviewWeightMin = wght.MinValue;
                PreviewWeightMax = wght.MaxValue;
                PreviewWeightValue = Math.Clamp(targetWeight, wght.MinValue, wght.MaxValue);
            }
            else
            {
                PreviewWeightMin = 100;
                PreviewWeightMax = 900;
                PreviewWeightValue = targetWeight;
            }

            var wdth = value.VariationAxes.FirstOrDefault(a => a.Tag == "wdth");
            PreviewWidthScale = wdth is not null ? wdth.DefaultValue : 100.0;

            var slnt = value.VariationAxes.FirstOrDefault(a => a.Tag == "slnt");
            HasSlantAxis = slnt is not null;
            if (slnt is not null)
            {
                // slnt 軸の最小・最大角度を設定（通常 min は負数、max は 0 または正数）
                PreviewSlantMin = Math.Min(slnt.MinValue, slnt.MaxValue);
                PreviewSlantMax = Math.Max(slnt.MinValue, slnt.MaxValue);
                PreviewSlantAngle = slnt.DefaultValue;
            }
            else
            {
                PreviewSlantMin = -20;
                PreviewSlantMax = 20;
                PreviewSlantAngle = 0.0;
            }

            var ital = value.VariationAxes.FirstOrDefault(a => a.Tag == "ital");
            HasItalicAxis = ital is not null;
            PreviewIsItalic = value.Style != FontStyles.Normal || (ital is not null && ital.DefaultValue > 0.5);
        }
        else
        {
            CurrentVariationAxes.Clear();
            VariableAxesSummary = value.IsVariableFont ? "可変軸情報なし" : "非対応（固定デザイン）";
            InfoIsVariableText = value.IsVariableFont ? "対応" : "非対応";

            PreviewWeightMin = 100;
            PreviewWeightMax = 900;
            PreviewWeightValue = value.Weight.ToOpenTypeWeight();
            PreviewWidthScale = 100.0;
            PreviewSlantMin = -20;
            PreviewSlantMax = 20;
            PreviewSlantAngle = 0.0;
            HasSlantAxis = false;
            HasItalicAxis = false;
            PreviewIsItalic = value.Style != FontStyles.Normal;
        }

        // OpenType 機能および収録文字セットの日本語サマリー
        var featureTags = new List<string>();
        if (value.HasJisLevel3And4) featureTags.Add("JIS第1〜第4水準");
        else if (value.HasJisLevel2) featureTags.Add("JIS第1〜第2水準");
        else if (value.HasJisLevel1) featureTags.Add("JIS第1水準");

        if (value.HasIvs) featureTags.Add("異体字(IVS)");
        if (value.HasEmoji) featureTags.Add("絵文字");
        if (value.HasLigatures) featureTags.Add("合字");
        if (value.HasSlashedZero) featureTags.Add("斜線ゼロ");
        if (value.HasSmallCaps) featureTags.Add("スモールキャップス");
        if (value.HasOldStyleNumerals) featureTags.Add("オールドスタイル数字");
        if (value.HasStylisticSets) featureTags.Add("異体字セット");
        InfoFeatures = featureTags.Count > 0 ? string.Join(", ", featureTags) : "—";

        // ライセンス（長い場合は省略）
        InfoLicense = string.IsNullOrEmpty(value.License) ? "—"
            : value.License.Length > 100 ? value.License[..100] + "…" : value.License;

        // 所属ファミリーの書体数とツールチップ
        if (SelectedFamily is null || SelectedFamily.FamilyName != value.FamilyName)
        {
            var fam = _allFonts.FirstOrDefault(f => f.FamilyName == value.FamilyName);
            if (fam is not null && !ReferenceEquals(_selectedFamily, fam))
            {
                _selectedFamily = fam;
                OnPropertyChanged(nameof(SelectedFamily));
            }
        }

        InfoTypefaceCount = SelectedFamily?.TypefaceCountDisplay ?? value.FamilyTypefaceCount;
        InfoTypefacesTooltip = SelectedFamily?.Typefaces is not null
            ? string.Join("\n", SelectedFamily.Typefaces.Select(t => $"• {t.DisplayName}"))
            : value.FamilyTypefacesTooltip;
    }

    [RelayCommand]
    private void ResetPreviewAdjustments()
    {
        PreviewFontSize = 24.0;
        if (SelectedTypeface is null)
        {
            PreviewWeightValue = 400.0;
            PreviewWidthScale = 100.0;
            PreviewSlantAngle = 0.0;
            HasSlantAxis = false;
            PreviewIsItalic = false;
            return;
        }

        if (SelectedTypeface.IsVariableFont && SelectedTypeface.VariationAxes.Count > 0)
        {
            var wght = SelectedTypeface.VariationAxes.FirstOrDefault(a => a.Tag == "wght");
            var targetWeight = (double)SelectedTypeface.Weight.ToOpenTypeWeight();
            PreviewWeightValue = wght is not null
                ? Math.Clamp(targetWeight, wght.MinValue, wght.MaxValue)
                : targetWeight;

            var wdth = SelectedTypeface.VariationAxes.FirstOrDefault(a => a.Tag == "wdth");
            PreviewWidthScale = wdth?.DefaultValue ?? 100.0;

            var slnt = SelectedTypeface.VariationAxes.FirstOrDefault(a => a.Tag == "slnt");
            HasSlantAxis = slnt is not null;
            if (slnt is not null)
            {
                PreviewSlantMin = Math.Min(slnt.MinValue, slnt.MaxValue);
                PreviewSlantMax = Math.Max(slnt.MinValue, slnt.MaxValue);
                PreviewSlantAngle = slnt.DefaultValue;
            }
            else
            {
                PreviewSlantAngle = 0.0;
            }

            var ital = SelectedTypeface.VariationAxes.FirstOrDefault(a => a.Tag == "ital");
            PreviewIsItalic = SelectedTypeface.Style != FontStyles.Normal || (ital is not null && ital.DefaultValue > 0.5);
        }
        else
        {
            PreviewWeightValue = SelectedTypeface.Weight.ToOpenTypeWeight();
            PreviewWidthScale = 100.0;
            PreviewSlantAngle = 0.0;
            HasSlantAxis = false;
            PreviewIsItalic = SelectedTypeface.Style != FontStyles.Normal;
        }
    }

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
        CurrentTheme = themeKey;
        IsDarkTheme = themeKey != "light";
        OnPropertyChanged(nameof(CurrentThemeDisplayName));
        StatusText = $"🎨 テーマを切り替えました: {CurrentThemeDisplayName}";
    }

    private bool _isSettingSampleTextProgrammatically;

    partial void OnPreviewTextChanged(string value)
    {
        if (!_isSettingSampleTextProgrammatically)
        {
            _appliedSampleLanguage = null;
            OnPropertyChanged(nameof(IsSampleTextMismatched));
        }
    }

    [RelayCommand]
    private void ApplyEmojiSampleText()
    {
        _isSettingSampleTextProgrammatically = true;
        try
        {
            PreviewText = SampleTextProvider.EmojiSampleText;
            _appliedSampleLanguage = "絵文字";
            OnPropertyChanged(nameof(IsSampleTextMismatched));
        }
        finally
        {
            _isSettingSampleTextProgrammatically = false;
        }
    }

    [RelayCommand]
    private void ApplyLanguageSampleText()
    {
        if (SelectedCharSet == CharSetEmoji)
        {
            ApplyEmojiSampleText();
            return;
        }

        var targetLang = GetCurrentTargetLanguage();

        _isSettingSampleTextProgrammatically = true;
        try
        {
            PreviewText = SampleTextProvider.GetSampleTextForLanguage(targetLang);
            _appliedSampleLanguage = targetLang;
            OnPropertyChanged(nameof(IsSampleTextMismatched));
        }
        finally
        {
            _isSettingSampleTextProgrammatically = false;
        }
    }

    [RelayCommand]
    private void ClearFilters()
    {
        SearchText = string.Empty;
        SelectedWeight = SelectedStyle = SelectedStretch = AllOption;
        SelectedLanguage = SelectedSpacing = SelectedVendor = AllOption;
        SelectedCategory = SelectedFileFormat = SelectedOutlineFormat = SelectedInstallType = SelectedCharSet = AllOption;
        FilterNerdFonts = FilterLigatures = FilterSlashedZero = FilterVariableFont = FilterIvs = false;
    }

    [RelayCommand]
    private void SelectFilterLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language)) return;

        if (string.Equals(SelectedLanguage, language, StringComparison.OrdinalIgnoreCase))
        {
            SelectedLanguage = AllOption;
            StatusText = $"🔍 言語フィルターを解除しました（すべて: {FilteredFamilyCount} ファミリー）";
        }
        else
        {
            SelectedLanguage = language;
            StatusText = $"🔍 言語「{language}」でフィルター中 ({FilteredFamilyCount} ファミリー / {FilteredFontCount} 書体)";
        }
    }

    private void UpdateInfoLanguageTags()
    {
        if (SelectedTypeface is null || SelectedTypeface.Languages.Count == 0)
        {
            InfoLanguageTags = [];
            HasLanguages = false;
            return;
        }

        var tags = SelectedTypeface.Languages.Select(lang => new LanguageTagItem
        {
            Name = lang,
            IsSelected = string.Equals(lang, SelectedLanguage, StringComparison.OrdinalIgnoreCase)
        }).ToList();

        InfoLanguageTags = new ObservableCollection<LanguageTagItem>(tags);
        HasLanguages = true;
    }

    // ═══════════════════════════════════════════════════════
    // クリップボードコピーコマンド
    // ═══════════════════════════════════════════════════════

    [RelayCommand]
    private void CopyFontFamily(object? target)
    {
        var familyName = target switch
        {
            FontFamilyInfo f => f.FamilyName,
            TypefaceInfo tf => tf.FamilyName,
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
            TypefaceInfo tf => string.IsNullOrWhiteSpace(tf.StyleName) ? tf.FamilyName : $"{tf.FamilyName} {tf.StyleName}".Trim(),
            _ => !string.IsNullOrWhiteSpace(style) && !string.IsNullOrWhiteSpace(family) ? $"{family} {style}".Trim() : family
        };
        if (string.IsNullOrWhiteSpace(fontName)) return;

        CopyToClipboard(fontName);
        StatusText = $"📋 フォント名をコピーしました: {fontName}";
    }

    [RelayCommand]
    private void CopyDisplayName(object? target) => CopyFontName(target);

    [RelayCommand]
    private void CopyCss(object? target)
    {
        var (familyName, category, spacing) = target switch
        {
            FontFamilyInfo f => (f.FamilyName, f.Category, f.Spacing),
            TypefaceInfo tf => (tf.FamilyName, tf.Category, tf.Spacing),
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
            FontFamilyInfo f => f.FilePath,
            TypefaceInfo tf => tf.FilePath,
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

    // ═══════════════════════════════════════════════════════
    // フィルタリング処理
    // ═══════════════════════════════════════════════════════

    private const int FilterIdxWeight = 0;
    private const int FilterIdxStyle = 1;
    private const int FilterIdxStretch = 2;
    private const int FilterIdxLanguage = 3;
    private const int FilterIdxSpacing = 4;
    private const int FilterIdxVendor = 5;
    private const int FilterIdxCategory = 6;
    private const int FilterIdxFileFormat = 7;
    private const int FilterIdxOutlineFormat = 8;
    private const int FilterIdxInstallType = 9;
    private const int FilterIdxCharSet = 10;

    /// <summary>
    /// 単一の書体（TypefaceInfo）が、指定された除外インデックス以外の全アクティブフィルターに合致するか判定します。
    /// </summary>
    private bool TypefaceMatchesFilters(TypefaceInfo tf, int excludeIndex = -1)
    {
        if (excludeIndex != FilterIdxWeight && !string.IsNullOrEmpty(SelectedWeight) && SelectedWeight != AllOption && tf.WeightDisplay != SelectedWeight)
            return false;
        if (excludeIndex != FilterIdxStyle && !string.IsNullOrEmpty(SelectedStyle) && SelectedStyle != AllOption && tf.Style.ToString() != SelectedStyle)
            return false;
        if (excludeIndex != FilterIdxStretch && !string.IsNullOrEmpty(SelectedStretch) && SelectedStretch != AllOption && tf.Stretch.ToString() != SelectedStretch)
            return false;
        if (excludeIndex != FilterIdxLanguage && !string.IsNullOrEmpty(SelectedLanguage) && SelectedLanguage != AllOption && !tf.Languages.Contains(SelectedLanguage))
            return false;
        if (excludeIndex != FilterIdxFileFormat && !string.IsNullOrEmpty(SelectedFileFormat) && SelectedFileFormat != AllOption && tf.FileFormat != SelectedFileFormat)
            return false;
        if (excludeIndex != FilterIdxOutlineFormat && !string.IsNullOrEmpty(SelectedOutlineFormat) && SelectedOutlineFormat != AllOption && tf.OutlineFormat != SelectedOutlineFormat)
            return false;
        if (excludeIndex != FilterIdxInstallType && !string.IsNullOrEmpty(SelectedInstallType) && SelectedInstallType != AllOption && tf.InstallType != SelectedInstallType)
            return false;
        if (excludeIndex != FilterIdxCharSet && !string.IsNullOrEmpty(SelectedCharSet) && SelectedCharSet != AllOption)
        {
            var matchCharSet = SelectedCharSet switch
            {
                CharSetJis1 => tf.HasJisLevel1,
                CharSetJis2 => tf.HasJisLevel2,
                CharSetJis34 => tf.HasJisLevel3And4,
                CharSetEmoji => tf.HasEmoji,
                _ => true
            };
            if (!matchCharSet) return false;
        }

        if (FilterNerdFonts && !tf.HasNerdFonts) return false;
        if (FilterLigatures && !tf.HasLigatures) return false;
        if (FilterSlashedZero && !tf.HasSlashedZero) return false;
        if (FilterVariableFont && !tf.IsVariableFont) return false;
        if (FilterIvs && !tf.HasIvs) return false;

        return true;
    }

    /// <summary>
    /// ファミリー内の全書体について、現在のフィルター条件への適合フラグ（IsFilterMatched）を更新します。
    /// プルダウン内で非適合書体をグレーアウト（選択不可）表示するために利用されます。
    /// </summary>
    private void UpdateTypefacesFilterMatch(FontFamilyInfo? family)
    {
        if (family is null) return;
        foreach (var tf in family.Typefaces)
        {
            tf.IsFilterMatched = TypefaceMatchesFilters(tf);
        }
    }

    private bool _isUpdatingFilters;

    private void ApplyFilters()
    {
        if (_isUpdatingFilters || _allFonts.Count == 0) return;
        _isUpdatingFilters = true;
        try
        {
            // 1. 基本フィルタ（テキスト検索 + チェックボックス5種）
            var baseQuery = _allFonts.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                var searchTerms = SearchText.Split([' ', '　', '\t'], StringSplitOptions.RemoveEmptyEntries);
                if (searchTerms.Length > 0)
                {
                    baseQuery = baseQuery.Where(fam =>
                    {
                        return searchTerms.All(term =>
                            fam.FamilyName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                            fam.AllFamilyNames.Any(name => name.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                            fam.Vendor.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                            fam.Typefaces.Any(tf =>
                                tf.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                                tf.FilePath.Contains(term, StringComparison.OrdinalIgnoreCase)));
                    });
                }
            }

            if (FilterNerdFonts)
                baseQuery = baseQuery.Where(fam => fam.HasNerdFonts);
            if (FilterLigatures)
                baseQuery = baseQuery.Where(fam => fam.HasLigatures);
            if (FilterSlashedZero)
                baseQuery = baseQuery.Where(fam => fam.HasSlashedZero);
            if (FilterVariableFont)
                baseQuery = baseQuery.Where(fam => fam.IsVariableFont);
            if (FilterIvs)
                baseQuery = baseQuery.Where(fam => fam.HasIvs);

            var baseList = baseQuery.ToList();

            // 2. 各ドロップダウンのフィルタ判定用関数
            bool MatchesAllExcept(FontFamilyInfo fam, int excludeIndex)
            {
                if (excludeIndex != FilterIdxSpacing && !string.IsNullOrEmpty(SelectedSpacing) && SelectedSpacing != AllOption && fam.Spacing != SelectedSpacing) return false;
                if (excludeIndex != FilterIdxVendor && !string.IsNullOrEmpty(SelectedVendor) && SelectedVendor != AllOption && fam.Vendor != SelectedVendor) return false;
                if (excludeIndex != FilterIdxCategory && !string.IsNullOrEmpty(SelectedCategory) && SelectedCategory != AllOption && fam.Category != SelectedCategory) return false;

                return fam.Typefaces.Any(tf => TypefaceMatchesFilters(tf, excludeIndex));
            }

            // 3. 全フィルタ条件を満たすフォント一覧（結果リスト）
            var filtered = baseList.Where(fam => MatchesAllExcept(fam, -1))
                .OrderBy(fam => fam.FamilyName)
                .ToList();

            // 検索結果の更新（選択中のフォントファミリーを保持）
            var previousFamily = SelectedFamily;
            FilteredFamilies = new ObservableCollection<FontFamilyInfo>(filtered);
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

            // 選択中ファミリー内の書体のフィルター合致状態（グレーアウト用）を更新
            UpdateTypefacesFilterMatch(SelectedFamily);

            // フィルター条件変更に伴い、選択中書体が非合致またはファミリー外の場合は最適な書体を再選択
            if (SelectedFamily is not null)
            {
                if (SelectedTypeface is null ||
                    !SelectedTypeface.IsFilterMatched ||
                    !SelectedFamily.Typefaces.Contains(SelectedTypeface))
                {
                    SelectedTypeface = SelectedFamily.FindBestMatchingTypeface(
                        SelectedWeight, SelectedStyle, SelectedStretch,
                        SelectedLanguage, SelectedCharSet,
                        FilterNerdFonts, FilterLigatures, FilterSlashedZero,
                        FilterVariableFont, FilterIvs);
                }
            }
            else
            {
                SelectedTypeface = null;
            }

            // 4. 各ドロップダウンの選択肢を更新
            // 「自身のフィルタ条件を除外したリスト」から選択肢を生成することで、
            // 項目選択後もプルダウンを開いた際に他の選択肢へ直接切り替えられるようにします。

            var availableWeights = baseList.Where(fam => MatchesAllExcept(fam, FilterIdxWeight))
                .SelectMany(fam => fam.Typefaces.Where(tf => TypefaceMatchesFilters(tf, FilterIdxWeight)))
                .GroupBy(tf => tf.WeightDisplay)
                .OrderBy(g => g.First().Weight.ToOpenTypeWeight())
                .Select(g => g.Key)
                .ToList();
            AvailableWeights = new ObservableCollection<string>([AllOption, .. availableWeights]);

            var availableStyles = baseList.Where(fam => MatchesAllExcept(fam, FilterIdxStyle))
                .SelectMany(fam => fam.Typefaces.Where(tf => TypefaceMatchesFilters(tf, FilterIdxStyle)))
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
            AvailableStyles = new ObservableCollection<string>([AllOption, .. availableStyles]);
            UpdateDropdown(baseList.Where(fam => MatchesAllExcept(fam, FilterIdxStretch)), fam => fam.Typefaces.Where(tf => TypefaceMatchesFilters(tf, FilterIdxStretch)).Select(tf => tf.Stretch.ToString()), v => AvailableStretches = v);
            UpdateDropdown(baseList.Where(fam => MatchesAllExcept(fam, FilterIdxLanguage)), fam => fam.Typefaces.Where(tf => TypefaceMatchesFilters(tf, FilterIdxLanguage)).SelectMany(tf => tf.Languages), v => AvailableLanguages = v);
            UpdateDropdown(baseList.Where(fam => MatchesAllExcept(fam, FilterIdxSpacing)), fam => fam.Typefaces.Where(tf => TypefaceMatchesFilters(tf, FilterIdxSpacing)).Select(tf => tf.Spacing), v => AvailableSpacings = v, skipEmpty: true);
            UpdateDropdownSingle(baseList.Where(fam => MatchesAllExcept(fam, FilterIdxVendor)), fam => fam.Vendor, v => AvailableVendors = v, skipEmpty: true);
            UpdateDropdownSingle(baseList.Where(fam => MatchesAllExcept(fam, FilterIdxCategory)), fam => fam.Category, v => AvailableCategories = v, skipEmpty: true);
            UpdateDropdown(baseList.Where(fam => MatchesAllExcept(fam, FilterIdxFileFormat)), fam => fam.Typefaces.Where(tf => TypefaceMatchesFilters(tf, FilterIdxFileFormat)).Select(tf => tf.FileFormat), v => AvailableFileFormats = v, skipEmpty: true);
            UpdateDropdown(baseList.Where(fam => MatchesAllExcept(fam, FilterIdxOutlineFormat)), fam => fam.Typefaces.Where(tf => TypefaceMatchesFilters(tf, FilterIdxOutlineFormat)).Select(tf => tf.OutlineFormat), v => AvailableOutlineFormats = v, skipEmpty: true);
            UpdateDropdown(baseList.Where(fam => MatchesAllExcept(fam, FilterIdxInstallType)), fam => fam.Typefaces.Where(tf => TypefaceMatchesFilters(tf, FilterIdxInstallType)).Select(tf => tf.InstallType), v => AvailableInstallTypes = v, skipEmpty: true);
        }
        finally
        {
            _isUpdatingFilters = false;
        }
    }

    private static void UpdateDropdown(
        IEnumerable<FontFamilyInfo> filtered,
        Func<FontFamilyInfo, IEnumerable<string>> selector,
        Action<ObservableCollection<string>> setter,
        bool skipEmpty = false)
    {
        var available = filtered.SelectMany(selector)
            .Where(v => !skipEmpty || (!string.IsNullOrEmpty(v) && v != "不明"))
            .Distinct().OrderBy(v => v).ToList();
        setter(new ObservableCollection<string>([AllOption, .. available]));
    }

    private static void UpdateDropdownSingle(
        IEnumerable<FontFamilyInfo> filtered,
        Func<FontFamilyInfo, string> selector,
        Action<ObservableCollection<string>> setter,
        bool skipEmpty = false)
    {
        var available = filtered.Select(selector)
            .Where(v => !skipEmpty || (!string.IsNullOrEmpty(v) && v != "不明"))
            .Distinct().OrderBy(v => v).ToList();
        setter(new ObservableCollection<string>([AllOption, .. available]));
    }
}
