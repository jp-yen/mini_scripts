using System;
using System.Collections.Generic;
using System.Linq;

namespace FontSelector.Models;

public enum FontFilterType
{
    None,
    Weight,
    Style,
    Stretch,
    Language,
    Spacing,
    Vendor,
    Category,
    FileFormat,
    OutlineFormat,
    InstallType,
    CharSet
}

public class FontFilterCriteria
{
    public string SearchText { get; set; } = string.Empty;
    public string SelectedWeight { get; set; } = "すべて";
    public string SelectedStyle { get; set; } = "すべて";
    public string SelectedStretch { get; set; } = "すべて";
    public string SelectedLanguage { get; set; } = "すべて";
    public string SelectedSpacing { get; set; } = "すべて";
    public string SelectedVendor { get; set; } = "すべて";
    public string SelectedCategory { get; set; } = "すべて";
    public string SelectedFileFormat { get; set; } = "すべて";
    public string SelectedOutlineFormat { get; set; } = "すべて";
    public string SelectedInstallType { get; set; } = "すべて";
    public string SelectedCharSet { get; set; } = "すべて";
    public bool FilterNerdFonts { get; set; }
    public bool FilterLigatures { get; set; }
    public bool FilterSlashedZero { get; set; }
    public bool FilterVariableFont { get; set; }
    public bool FilterIvs { get; set; }

    public IEnumerable<FontFamilyInfo> ApplyBaseQuery(IEnumerable<FontFamilyInfo> fonts)
    {
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            return fonts;
        }

        var terms = SearchText.Split([' ', '　'], StringSplitOptions.RemoveEmptyEntries);
        return fonts.Where(f => terms.All(term =>
            f.FamilyName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            f.AllFamilyNames.Any(n => n.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
            (f.Vendor?.Contains(term, StringComparison.OrdinalIgnoreCase) == true)));
    }

    private static bool IsFiltered(string? value) => !string.IsNullOrWhiteSpace(value) && value != "すべて";

    public bool MatchesFamily(FontFamilyInfo fam, FontFilterType skipFilter = FontFilterType.None)
    {
        if (skipFilter != FontFilterType.Vendor && IsFiltered(SelectedVendor) && fam.Vendor != SelectedVendor) return false;
        if (skipFilter != FontFilterType.Category && IsFiltered(SelectedCategory) && fam.Category != SelectedCategory) return false;
        
        return fam.Typefaces.Any(tf => MatchesTypeface(tf, skipFilter));
    }

    public bool MatchesTypeface(TypefaceInfo tf, FontFilterType skipFilter = FontFilterType.None)
    {
        if (skipFilter != FontFilterType.Weight && IsFiltered(SelectedWeight) && tf.WeightDisplay != SelectedWeight) return false;
        if (skipFilter != FontFilterType.Style && IsFiltered(SelectedStyle) && tf.Style.ToString() != SelectedStyle) return false;
        if (skipFilter != FontFilterType.Stretch && IsFiltered(SelectedStretch) && tf.Stretch.ToString() != SelectedStretch) return false;
        if (skipFilter != FontFilterType.Language && IsFiltered(SelectedLanguage) && !tf.Languages.Any(l => l == SelectedLanguage)) return false;
        if (skipFilter != FontFilterType.Spacing && IsFiltered(SelectedSpacing) && tf.Spacing != SelectedSpacing) return false;
        if (skipFilter != FontFilterType.FileFormat && IsFiltered(SelectedFileFormat) && tf.FileFormat != SelectedFileFormat) return false;
        if (skipFilter != FontFilterType.OutlineFormat && IsFiltered(SelectedOutlineFormat) && tf.OutlineFormat != SelectedOutlineFormat) return false;
        if (skipFilter != FontFilterType.InstallType && IsFiltered(SelectedInstallType) && tf.InstallType != SelectedInstallType) return false;
        if (skipFilter != FontFilterType.CharSet && IsFiltered(SelectedCharSet))
        {
            var matchCharSet = SelectedCharSet switch
            {
                "JIS第1水準 (常用漢字)" or "JIS第1水準" => tf.HasJisLevel1,
                "JIS第2水準 (人名・旧字)" or "JIS第2水準" => tf.HasJisLevel2,
                "JIS第3・第4水準 (JIS2004)" or "JIS第3・第4水準" => tf.HasJisLevel3And4,
                "絵文字 (Emoji)" or "絵文字" => tf.HasEmoji,
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
}
