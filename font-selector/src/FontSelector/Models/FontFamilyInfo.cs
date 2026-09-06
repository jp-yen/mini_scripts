using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace FontSelector.Models;

/// <summary>
/// Represents a font family with its associated typefaces (weight/style/stretch variations).
/// Extended with family-level aggregated metadata for listing, filtering, and previewing.
/// </summary>
public class FontFamilyInfo
{
    public string FamilyName { get; }
    public string Source { get; }
    public IReadOnlyList<TypefaceInfo> Typefaces { get; }

    private TypefaceInfo? _defaultTypeface;
    private IReadOnlyList<string>? _languages;

    public FontFamilyInfo(string familyName, IReadOnlyList<TypefaceInfo> typefaces, string source = "")
    {
        FamilyName = familyName;
        Source = string.IsNullOrEmpty(source) ? familyName : source;
        Typefaces = [.. typefaces
            .OrderBy(t => t.Weight.ToOpenTypeWeight())
            .ThenBy(t => GetStyleRank(t.Style))
            .ThenBy(t => Math.Abs(t.Stretch.ToOpenTypeStretch() - 5))
            .ThenBy(t => t.Stretch.ToOpenTypeStretch())
            .ThenBy(t => t.StyleName, StringComparer.OrdinalIgnoreCase)];
    }

    public static int GetStyleRank(FontStyle style)
    {
        if (style == FontStyles.Normal) return 0;
        if (style == FontStyles.Italic) return 1;
        if (style == FontStyles.Oblique) return 2;
        return 3;
    }

    /// <summary>ファミリー内の代表書体（Regular / Normal 400 を優先）</summary>
    public TypefaceInfo DefaultTypeface => _defaultTypeface ??= FindDefaultTypeface();

    /// <summary>所属書体数の表示（例: "18 書体"）</summary>
    public string TypefaceCountDisplay => $"{Typefaces.Count} 書体";

    /// <summary>日本語名・英語名を含む別名コレクション（検索用）</summary>
    public IReadOnlyList<string> AllFamilyNames =>
        Typefaces.Count > 0 ? Typefaces[0].AllFamilyNames : [FamilyName];

    // ── 集約メタデータ ──
    public string Category => DefaultTypeface.Category;
    public IReadOnlyList<string> Languages => _languages ??= [.. Typefaces.SelectMany(typeface => typeface.Languages).Distinct()];
    public string Spacing => DefaultTypeface.Spacing;
    public string Vendor => DefaultTypeface.Vendor;
    public string InstallType => DefaultTypeface.InstallType;
    public string FileFormat => DefaultTypeface.FileFormat;
    public string OutlineFormat => DefaultTypeface.OutlineFormat;
    public string FilePath => DefaultTypeface.FilePath;

    // ── 機能フラグ（いずれかの書体が対応していれば true） ──
    public bool IsVariableFont => Typefaces.Any(typeface => typeface.IsVariableFont);
    public bool HasNerdFonts => Typefaces.Any(typeface => typeface.HasNerdFonts);
    public bool HasLigatures => Typefaces.Any(typeface => typeface.HasLigatures);
    public bool HasSlashedZero => Typefaces.Any(typeface => typeface.HasSlashedZero);
    public bool HasIvs => Typefaces.Any(typeface => typeface.HasIvs);
    public bool HasEmoji => Typefaces.Any(typeface => typeface.HasEmoji);
    public bool HasJisLevel1 => Typefaces.Any(typeface => typeface.HasJisLevel1);
    public bool HasJisLevel2 => Typefaces.Any(typeface => typeface.HasJisLevel2);
    public bool HasJisLevel3And4 => Typefaces.Any(typeface => typeface.HasJisLevel3And4);

    /// <summary>
    /// フィルター条件に最も適合する書体をファミリー内から検索して返します。
    /// 条件指定がない場合は DefaultTypeface を返します。
    /// </summary>
    public TypefaceInfo FindBestMatchingTypeface(FontFilterCriteria? criteria)
    {
        if (criteria is null) return DefaultTypeface;

        return FindBestMatchingTypeface(
            weightFilter: criteria.SelectedWeight,
            styleFilter: criteria.SelectedStyle,
            stretchFilter: criteria.SelectedStretch,
            languageFilter: criteria.SelectedLanguage,
            charSetFilter: criteria.SelectedCharSet,
            filterNerdFonts: criteria.FilterNerdFonts,
            filterLigatures: criteria.FilterLigatures,
            filterSlashedZero: criteria.FilterSlashedZero,
            filterVariableFont: criteria.FilterVariableFont,
            filterIvs: criteria.FilterIvs);
    }

    /// <summary>
    /// フィルター条件に最も適合する書体をファミリー内から検索して返します。
    /// 条件指定がない場合は DefaultTypeface を返します。
    /// </summary>
    public TypefaceInfo FindBestMatchingTypeface(
        string? weightFilter = null,
        string? styleFilter = null,
        string? stretchFilter = null,
        string? languageFilter = null,
        string? charSetFilter = null,
        bool filterNerdFonts = false,
        bool filterLigatures = false,
        bool filterSlashedZero = false,
        bool filterVariableFont = false,
        bool filterIvs = false)
    {
        if (Typefaces.Count == 0) return DefaultTypeface;

        IEnumerable<TypefaceInfo> candidates = Typefaces;

        // 1. 言語フィルタ
        if (!string.IsNullOrEmpty(languageFilter) && languageFilter != "すべて")
        {
            var matched = candidates.Where(t => t.Languages.Contains(languageFilter)).ToList();
            if (matched.Count > 0) candidates = matched;
        }

        // 2. 文字セットフィルタ
        if (!string.IsNullOrEmpty(charSetFilter) && charSetFilter != "すべて")
        {
            var matched = charSetFilter switch
            {
                "JIS第1水準 (常用漢字)" or "JIS第1水準" => candidates.Where(t => t.HasJisLevel1).ToList(),
                "JIS第2水準 (人名・旧字)" or "JIS第2水準" => candidates.Where(t => t.HasJisLevel2).ToList(),
                "JIS第3・第4水準 (JIS2004)" or "JIS第3・第4水準" => candidates.Where(t => t.HasJisLevel3And4).ToList(),
                "絵文字 (Emoji)" or "絵文字" => candidates.Where(t => t.HasEmoji).ToList(),
                _ => candidates.ToList()
            };
            if (matched.Count > 0) candidates = matched;
        }

        // 3. 各種機能チェックボックス
        if (filterNerdFonts)
        {
            var matched = candidates.Where(typeface => typeface.HasNerdFonts).ToList();
            if (matched.Count > 0) candidates = matched;
        }
        if (filterLigatures)
        {
            var matched = candidates.Where(typeface => typeface.HasLigatures).ToList();
            if (matched.Count > 0) candidates = matched;
        }
        if (filterSlashedZero)
        {
            var matched = candidates.Where(typeface => typeface.HasSlashedZero).ToList();
            if (matched.Count > 0) candidates = matched;
        }
        if (filterVariableFont)
        {
            var matched = candidates.Where(typeface => typeface.IsVariableFont).ToList();
            if (matched.Count > 0) candidates = matched;
        }
        if (filterIvs)
        {
            var matched = candidates.Where(typeface => typeface.HasIvs).ToList();
            if (matched.Count > 0) candidates = matched;
        }

        // 4. 太さ・スタイル・ストレッチ
        if (!string.IsNullOrEmpty(styleFilter) && styleFilter != "すべて")
        {
            var matched = candidates.Where(typeface => typeface.Style.ToString() == styleFilter).ToList();
            if (matched.Count > 0) candidates = matched;
        }

        if (!string.IsNullOrEmpty(weightFilter) && weightFilter != "すべて")
        {
            var matched = candidates.Where(typeface => typeface.WeightDisplay == weightFilter).ToList();
            if (matched.Count > 0) candidates = matched;
        }

        if (!string.IsNullOrEmpty(stretchFilter) && stretchFilter != "すべて")
        {
            var matched = candidates.Where(typeface => typeface.Stretch.ToString() == stretchFilter).ToList();
            if (matched.Count > 0) candidates = matched;
        }

        var list = candidates.ToList();

        // 優先度1: Normal/Regular(400) + Normal Style + Normal Stretch
        var regular = list.FirstOrDefault(t =>
            (t.Weight == FontWeights.Normal || t.Weight == FontWeights.Regular) &&
            t.Style == FontStyles.Normal &&
            t.Stretch == FontStretches.Normal);
        if (regular is not null) return regular;

        // 優先度2: Normal Style + Normal Stretch
        var normalStyleNormalStretch = list.FirstOrDefault(t => t.Style == FontStyles.Normal && t.Stretch == FontStretches.Normal);
        if (normalStyleNormalStretch is not null) return normalStyleNormalStretch;

        // 優先度3: Normal Stretch
        var normalStretch = list.FirstOrDefault(t => t.Stretch == FontStretches.Normal);
        if (normalStretch is not null) return normalStretch;

        // 優先度4: Normal Style
        var normalStyle = list.FirstOrDefault(t => t.Style == FontStyles.Normal);
        if (normalStyle is not null) return normalStyle;

        return list.FirstOrDefault() ?? DefaultTypeface;
    }

    private TypefaceInfo FindDefaultTypeface()
    {
        if (Typefaces.Count == 0)
            return new TypefaceInfo(new FontFamily(Source), FamilyName, new Typeface(FamilyName));

        // 1. Normal/Regular(400) + Normal Style + Normal Stretch
        var regular = Typefaces.FirstOrDefault(t =>
            (t.Weight == FontWeights.Normal || t.Weight == FontWeights.Regular) &&
            t.Style == FontStyles.Normal &&
            t.Stretch == FontStretches.Normal);
        if (regular is not null) return regular;

        // 2. Normal Style + Normal Stretch
        var normalStyle = Typefaces.FirstOrDefault(t => t.Style == FontStyles.Normal && t.Stretch == FontStretches.Normal);
        if (normalStyle is not null) return normalStyle;

        // 3. Normal Style
        var anyNormal = Typefaces.FirstOrDefault(t => t.Style == FontStyles.Normal);
        if (anyNormal is not null) return anyNormal;

        return Typefaces[0];
    }

    public override string ToString() => FamilyName;
}
