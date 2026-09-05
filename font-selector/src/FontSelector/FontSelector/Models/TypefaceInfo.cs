using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using FontSelector.Services;

namespace FontSelector.Models;

/// <summary>
/// Represents a single typeface (a specific weight/style/stretch variation within a font family).
/// Extended with metadata from GlyphTypeface and OpenType table analysis.
/// </summary>
public partial class TypefaceInfo : ObservableObject
{
    /// <summary>現在のフィルター条件に合致しているか（プルダウンで非合致書体をグレーアウト表示するために利用）</summary>
    [ObservableProperty] private bool _isFilterMatched = true;

    public string FamilyName { get; }
    public IReadOnlyList<string> AllFamilyNames { get; }
    public FontFamily FontFamily { get; }
    public FontWeight Weight { get; set; }
    public string WeightDisplay
    {
        get
        {
            var str = Weight.ToString();
            var val = Weight.ToOpenTypeWeight();
            // Weight.ToString() が既に数値（例: "250", "350"）の場合はそのまま数値を返し、
            // 名前（例: "Black", "Light", "Normal"）の場合は "Black (900)" のように数値を付加する
            return char.IsDigit(str[0]) ? str : $"{str} ({val})";
        }
    }
    public FontStyle Style { get; }
    public FontStretch Stretch { get; }
    public string DisplayName { get; set; }

    /// <summary>
    /// 軽量化された文字区間リスト（cmap から解析された文字範囲）。
    /// 同一フォントファイル間でインスタンスが共有され、メモリを最小限に抑えます。
    /// </summary>
    public IReadOnlyList<(uint start, uint end)>? CmapRanges { get; set; }

    /// <summary>
    /// フォントが指定された文字（32ビットUnicode絵文字含む）を収録しているか判定します。
    /// 二分探索（Binary Search）によりナノ秒単位・完全インメモリで高速判定します。
    /// </summary>
    public bool HasGlyph(int codePoint)
    {
        if (CmapRanges is not null && CmapRanges.Count > 0)
        {
            uint cp = (uint)codePoint;
            if (ContainsCodePoint(CmapRanges, cp))
                return true;

            // シンボルフォント / 私用領域エンコード (0xF020〜0xF0FF) の対応
            if (codePoint >= 0x20 && codePoint <= 0xFF)
            {
                uint symbolCp = 0xF000u | (uint)codePoint;
                if (ContainsCodePoint(CmapRanges, symbolCp))
                    return true;
            }

            return false;
        }

        // CmapRanges が取得できなかったフォントはフォールバックとして true
        return true;
    }

    private static bool ContainsCodePoint(IReadOnlyList<(uint start, uint end)> ranges, uint codePoint)
    {
        int left = 0, right = ranges.Count - 1;
        while (left <= right)
        {
            int mid = left + (right - left) / 2;
            var (start, end) = ranges[mid];
            if (codePoint >= start && codePoint <= end)
                return true;

            if (codePoint < start)
                right = mid - 1;
            else
                left = mid + 1;
        }
        return false;
    }

    // ── Language / Spacing / Vendor (Tier 0 — already existed) ──
    public IReadOnlyList<string> Languages { get; set; } = [];
    public string Spacing { get; set; } = "不明";
    public string Vendor { get; set; } = string.Empty;

    // ── Tier 1 ──
    /// <summary>File format: "TTF", "OTF", "TTC", or "不明"</summary>
    public string FileFormat { get; set; } = "不明";

    /// <summary>Install type: "システム", "ユーザー", or "不明"</summary>
    public string InstallType { get; set; } = "不明";

    // ── Tier 2 (OpenType Reader) ──
    public bool IsVariableFont { get; set; }
    public IReadOnlyList<VariationAxis> VariationAxes { get; set; } = [];
    public IReadOnlySet<string> OpenTypeFeatures { get; set; } = new HashSet<string>();

    /// <summary>Category: "明朝 / セリフ", "ゴシック / サンセリフ", "等幅", etc.</summary>
    public string Category { get; set; } = "不明";

    // ── Tier 3 ──
    public bool HasLigatures { get; set; }
    public bool HasSlashedZero { get; set; }
    public bool HasSmallCaps { get; set; }
    public bool HasOldStyleNumerals { get; set; }
    public bool HasStylisticSets { get; set; }
    public bool HasNerdFonts { get; set; }

    // ── Additional Fields ──
    public bool HasIvs { get; set; }
    public bool HasEmoji { get; set; }
    public bool HasJisLevel1 { get; set; }
    public bool HasJisLevel2 { get; set; }
    public bool HasJisLevel3And4 { get; set; }
    public string License { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string OutlineFormat { get; set; } = "不明";

    /// <summary>Font file path (for reference)</summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>所属ファミリー内の書体数表示（例: "3 書体"）</summary>
    public string FamilyTypefaceCount { get; set; } = string.Empty;

    /// <summary>所属ファミリー内の全書体一覧ツールチップ文字列</summary>
    public string FamilyTypefacesTooltip { get; set; } = string.Empty;

    public TypefaceInfo(FontFamily fontFamily, string familyName, Typeface typeface)
    {
        FontFamily = fontFamily;
        FamilyName = familyName;
        AllFamilyNames = [.. fontFamily.FamilyNames.Values.Distinct()];
        Weight = typeface.Weight;
        Style = typeface.Style;
        Stretch = typeface.Stretch;
        DisplayName = BuildDisplayName(familyName, typeface.Stretch, typeface.Weight, typeface.Style);
    }

    public void UpdateWeight(FontWeight newWeight)
    {
        Weight = newWeight;
        DisplayName = BuildDisplayName(FamilyName, Stretch, Weight, Style);
    }

    private static string BuildDisplayName(string familyName, FontStretch stretch, FontWeight weight, FontStyle style)
    {
        var parts = new List<string> { familyName };

        if (stretch != FontStretches.Normal)
            parts.Add(stretch.ToString());

        if (weight != FontWeights.Normal && weight != FontWeights.Regular)
            parts.Add(weight.ToString());
        else if (style == FontStyles.Normal && stretch == FontStretches.Normal)
            parts.Add("Regular");

        if (style != FontStyles.Normal)
            parts.Add(style.ToString());

        return string.Join(" ", parts);
    }

    /// <summary>ファミリー名を除いた書体スタイル名（例: "Regular", "Bold", "Oblique", "Condensed Bold" 等）</summary>
    public string StyleName => BuildStyleName(Stretch, Weight, Style);

    public static string BuildStyleName(FontStretch stretch, FontWeight weight, FontStyle style)
    {
        var parts = new List<string>();

        if (stretch != FontStretches.Normal)
            parts.Add(stretch.ToString());

        if (weight != FontWeights.Normal && weight != FontWeights.Regular)
            parts.Add(weight.ToString());
        else if (style == FontStyles.Normal && stretch == FontStretches.Normal)
            parts.Add("Regular");

        if (style != FontStyles.Normal)
            parts.Add(style.ToString());

        return parts.Count > 0 ? string.Join(" ", parts) : "Regular";
    }

    public override string ToString() => StyleName;
}
