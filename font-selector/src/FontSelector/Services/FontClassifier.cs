using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Media;
using FontSelector.Models;

namespace FontSelector.Services;

/// <summary>
/// Provides font classification, feature probing, and metadata extraction logic.
/// </summary>
public static class FontClassifier
{
    // 言語検出用の代表文字
    private static readonly Dictionary<string, int[]> LanguageProbeChars = new()
    {
        ["日本語"]             = [0x3042, 0x30A2, 0x3093],  // あ ア ん (かな文字)
        ["英語 / ラテン文字"]  = [0x0041, 0x0061, 0x0052],   // A a R
        ["韓国語"]             = [0xAC00, 0xB098, 0xD55C],   // 가 나 한 (ハングル)
        // 簡体字固有の文字（日本語・JISには含まれない文字で厳密に判定）
        // 说 (U+8BF4), 话 (U+8BDD), 语 (U+8BED), 这 (U+8FD9), 问 (U+95EE), 们 (U+4EEC), 张 (U+5F20)
        ["中国語 (簡体)"]      = [0x8BF4, 0x8BDD, 0x8BED, 0x8FD9, 0x95EE, 0x4EEC, 0x5F20],
        // 繁体字固有の文字（JIS水準外の繁体字圏常用字で厳密に判定）
        // 臺 (U+81FA), 灣 (U+7063), 體 (U+9AD4), 豐 (U+8C50), 麼 (U+9EBD), 唸 (U+5538)
        ["中国語 (繁体)"]      = [0x81FA, 0x7063, 0x9AD4, 0x8C50, 0x9EBD, 0x5538],
        ["キリル文字"]         = [0x0410, 0x0411, 0x0412],   // А Б В
        ["ギリシャ文字"]       = [0x0391, 0x0392, 0x0393],   // Α Β Γ
        ["アラビア文字"]       = [0x0627, 0x0628, 0x062A],   // ا ب ت
        ["ヘブライ文字"]       = [0x05D0, 0x05D1, 0x05D2],   // א ב ג
        ["タイ文字"]           = [0x0E01, 0x0E02, 0x0E03],   // ก ข ฃ
        ["デーヴァナーガリー"] = [0x0905, 0x0906, 0x0915],   // अ आ क
        ["ベンガル文字"]       = [0x0985, 0x0986, 0x0995],   // অ আ ক
        ["タミル文字"]         = [0x0B85, 0x0B86, 0x0B95],   // அ ஆ க
    };

    // Nerd Fonts PUA ranges across distinct icon sets:
    // 1. Devicons (Python, Rust, Go)
    private static readonly int[] NerdFontDeviconProbes = [0xE73C, 0xE7A8, 0xE627];
    // 2. Font Awesome / Git / Tool symbols (Git, Terminal, Folder, Check, Gear)
    private static readonly int[] NerdFontAwesomeProbes = [0xF1D3, 0xF120, 0xF07B, 0xF00C, 0xF013];
    // 3. Powerline symbols (Branch, Right triangle)
    private static readonly int[] NerdFontPowerlineProbes = [0xE0A0, 0xE0B0];

    // Spacing detection probe characters: space A W i m 1
    private static readonly int[] SpacingTestChars = [0x20, 0x41, 0x57, 0x69, 0x6D, 0x31];

    // Symbol Latin probe characters for PUA (0xF020〜0xF0FF): 'A', 'a', 'R', space, '1' (+ 0xF000)
    private static readonly int[] SymbolLatinProbes = [0xF041, 0xF061, 0xF052, 0xF020, 0xF031];

    // Known system font directories
    private static readonly string SystemFontDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts");
    private static readonly string UserFontDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                      "Microsoft", "Windows", "Fonts");

    public static List<string> DetectLanguages(GlyphTypeface glyphTypeface)
    {
        var languages = new List<string>();
        var map = glyphTypeface.CharacterToGlyphMap;

        foreach (var (lang, probeChars) in LanguageProbeChars)
        {
            var hits = probeChars.Count(c => map.ContainsKey(c));
            // 各言語の代表プローブ文字がすべて収録されていることを判定（誤判定を防止）
            var requiredHits = probeChars.Length;
            if (hits >= requiredHits)
                languages.Add(lang);
        }

        // シンボル / 私用領域 (PUA: 0xF020〜0xF0FF) に格納された旧規格の欧文フォント（&CenturyOldst 等）の検出
        if (!languages.Contains("英語 / ラテン文字"))
        {
            var puaHits = SymbolLatinProbes.Count(c => map.ContainsKey(c));
            if (puaHits >= 2 || map.Keys.Any(k => k >= 0xF020 && k <= 0xF07E))
            {
                languages.Add("英語 / ラテン文字 (私用領域・Symbol)");
            }
        }

        return languages;
    }

    public static string DetectSpacing(GlyphTypeface glyphTypeface)
    {
        try
        {
            var map = glyphTypeface.CharacterToGlyphMap;
            var widths = glyphTypeface.AdvanceWidths;

            var advanceWidths = new List<double>(SpacingTestChars.Length);
            foreach (var ch in SpacingTestChars)
            {
                if (map.TryGetValue(ch, out var glyphIndex) && widths.TryGetValue(glyphIndex, out var width))
                    advanceWidths.Add(width);
            }

            if (advanceWidths.Count < 4) return "不明";

            // advanceWidths[0] は空白文字 (0x20) なので除外、最初の可視文字 (advanceWidths[1]) の幅と比較
            var firstWidth = advanceWidths[1];
            for (int i = 2; i < advanceWidths.Count; i++)
            {
                if (Math.Abs(advanceWidths[i] - firstWidth) >= 0.001)
                    return "可変幅";
            }
            return "等幅";
        }
        catch { return "不明"; }
    }

    public static string GetVendor(GlyphTypeface glyphTypeface)
    {
        try
        {
            var names = glyphTypeface.ManufacturerNames;
            if (names != null && names.Count > 0)
            {
                if (names.TryGetValue(CultureInfo.GetCultureInfo("en-US"), out var enName) && !string.IsNullOrWhiteSpace(enName))
                    return enName.Trim();
                var first = names.Values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
                if (!string.IsNullOrWhiteSpace(first))
                    return first.Trim();
            }

            return string.Empty;
        }
        catch { return string.Empty; }
    }

    public static bool DetectNerdFonts(GlyphTypeface glyphTypeface)
    {
        var map = glyphTypeface.CharacterToGlyphMap;
        var hasDevicon = NerdFontDeviconProbes.Any(c => map.ContainsKey(c));
        if (!hasDevicon) return false;

        var hasPowerline = NerdFontPowerlineProbes.Any(c => map.ContainsKey(c));
        if (!hasPowerline) return false;

        var fontAwesomeHits = NerdFontAwesomeProbes.Count(c => map.ContainsKey(c));
        return fontAwesomeHits >= 2;
    }

    /// <summary>
    /// GlyphTypeface からフォントバージョンを取得し、整形済みバージョンと生のバージョン文字列を返します。
    /// OpenType 仕様に従い、セミコロン以降に付加されたビルドツール情報や、末尾に付加されたフォントファミリー名・括弧書き等を除去します。
    /// </summary>
    public static (string CleanVersion, string RawVersion) GetVersion(GlyphTypeface glyphTypeface, string? familyName = null)
    {
        try
        {
            var versions = glyphTypeface.VersionStrings;
            string? raw = null;
            if (versions is not null && versions.Count > 0)
            {
                if (versions.TryGetValue(CultureInfo.CurrentUICulture, out var uiVer))
                    raw = uiVer;
                else if (versions.TryGetValue(CultureInfo.GetCultureInfo("en-US"), out var enVer))
                    raw = enVer;
                else
                    raw = versions.Values.FirstOrDefault();
            }

            raw ??= glyphTypeface.Version.ToString("F3", CultureInfo.InvariantCulture);

            var candidateFamilyNames = new List<string>();
            if (!string.IsNullOrWhiteSpace(familyName))
                candidateFamilyNames.Add(familyName);

            if (glyphTypeface.FamilyNames is not null)
            {
                foreach (var fn in glyphTypeface.FamilyNames.Values)
                {
                    if (!string.IsNullOrWhiteSpace(fn))
                        candidateFamilyNames.Add(fn);
                }
            }
            if (glyphTypeface.Win32FamilyNames is not null)
            {
                foreach (var fn in glyphTypeface.Win32FamilyNames.Values)
                {
                    if (!string.IsNullOrWhiteSpace(fn))
                        candidateFamilyNames.Add(fn);
                }
            }

            return (CleanVersionString(raw, candidateFamilyNames), raw);
        }
        catch
        {
            var fallback = glyphTypeface.Version.ToString("F3", CultureInfo.InvariantCulture);
            return (fallback, fallback);
        }
    }

    /// <summary>
    /// バージョン文字列から不要なコンパイル情報、フォント名、ツール引数、内部コード等を除去し、正しいバージョン番号を返します。
    /// 例: "v2.0.0; ttfautohint (v1.8.4.7...) -l 6..." -> "v2.0.0"
    /// 例: "Version 1.200 (Monaspace Argon)" -> "Version 1.200"
    /// 例: "Ver.2.02 (c255-gmhp12-gmzp13-j0kei10-gmf14-gms14-gmn10-gme12-gmv`Ā ªª _" -> "Ver.2.02"
    /// </summary>
    public static string CleanVersionString(string? rawVersion, IEnumerable<string>? familyNames = null)
    {
        if (string.IsNullOrWhiteSpace(rawVersion))
            return string.Empty;

        // 0. 制御文字（改行、タブ、ヌル文字、0x00-0x1F, 0x7F-0x9F 等）や非表示文字を除去
        var cleanChars = rawVersion.Where(c => !char.IsControl(c)).ToArray();
        var v = new string(cleanChars).Trim();

        // 1. OpenType仕様: バージョン文字列に付加情報を含める場合はセミコロンで区切る
        var semiIdx = v.IndexOf(';');
        if (semiIdx >= 0)
        {
            v = v[..semiIdx].Trim();
        }

        // 2. 開き括弧 '(', '[', '{' がある場合、それ以降はビルドハッシュ・内部コード・ツール情報なので切り捨て
        var parenIdx = v.IndexOfAny(['(', '[', '{']);
        if (parenIdx >= 0)
        {
            v = v[..parenIdx].Trim();
        }

        // 3. セミコロンがなく直接ビルドツール名（ttfautohint等）が連結されている場合の対策
        var ttfIdx = v.IndexOf("ttfautohint", StringComparison.OrdinalIgnoreCase);
        if (ttfIdx > 0)
        {
            v = v[..ttfIdx].Trim().TrimEnd(';', '-');
        }

        // 4. フォントファミリー名が含まれている場合の除去（例: "Version 1.200 (Monaspace Argon)" や "Version 1.200 Monaspace Argon"）
        if (familyNames is not null)
        {
            foreach (var fn in familyNames.Where(f => !string.IsNullOrWhiteSpace(f)))
            {
                var trimmedFn = fn.Trim();
                v = Regex.Replace(v, $@"\s+{Regex.Escape(trimmedFn)}$", "", RegexOptions.IgnoreCase);
            }
        }

        // 5. 末尾の記号類を除去
        v = v.Trim().TrimEnd(';', ',', '-', ':', '/', '\\');

        // 6. "Version 1.20", "Ver.2.02", "v1.0.0", "1.00" などの先頭バージョン番号部分を正規表現でスマートに抽出
        var match = Regex.Match(v, @"^(?:Version|Ver\.?|v)?\s*\d+(?:\.\d+)+(?:[-_a-zA-Z0-9\.]+)?", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            v = match.Value.Trim();
        }

        return v;
    }

    public static string GetFilePath(GlyphTypeface glyphTypeface)
    {
        try
        {
            var uri = glyphTypeface.FontUri;
            if (uri is not null && uri.IsFile)
                return uri.LocalPath;
            return string.Empty;
        }
        catch { return string.Empty; }
    }

    public static string DetectFileFormat(string filePath)
    {
        if (string.IsNullOrEmpty(filePath)) return "不明";
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext switch
        {
            ".ttf" => "TTF",
            ".otf" => "OTF",
            ".ttc" or ".otc" => "TTC",
            _ => "不明"
        };
    }

    public static string DetectInstallType(string filePath)
    {
        if (string.IsNullOrEmpty(filePath)) return "不明";
        if (filePath.StartsWith(SystemFontDir, StringComparison.OrdinalIgnoreCase))
            return "システム";
        if (filePath.StartsWith(UserFontDir, StringComparison.OrdinalIgnoreCase))
            return "ユーザー";
        return "不明";
    }

    /// <summary>
    /// OS/2 の sFamilyClass、Panose データ、およびフォント名からフォント分類を判定
    /// </summary>
    public static string DetectCategory(OpenTypeReader otReader, string familyName, string spacing)
    {
        // 1. OS/2 の sFamilyClass から判定
        var familyClass = otReader.GetFamilyClass();
        if (familyClass >= 0)
        {
            var classId = familyClass >> 8; // 上位バイト = クラス
            var cat = classId switch
            {
                1 or 2 or 3 or 4 or 5 or 7 => "明朝 / セリフ",
                8 => "ゴシック / サンセリフ",
                9 => "装飾 / 見出し",
                10 => "手書き / 筆記体",
                12 => "記号 / シンボル",
                _ => null
            };
            if (cat is not null) return cat;
        }

        // 2. Panose から判定
        var panoseCat = ClassifyFromPanose(otReader);
        if (panoseCat != "不明") return panoseCat;

        // 3. フォント名キーワードから判定（メタデータ未設定のフォント救済）
        if (familyName.Contains("明朝", StringComparison.OrdinalIgnoreCase) ||
            familyName.Contains("Mincho", StringComparison.OrdinalIgnoreCase) ||
            familyName.Contains("Serif", StringComparison.OrdinalIgnoreCase))
        {
            return "明朝 / セリフ";
        }

        if (familyName.Contains("ゴシック", StringComparison.OrdinalIgnoreCase) ||
            familyName.Contains("Gothic", StringComparison.OrdinalIgnoreCase) ||
            familyName.Contains("Sans", StringComparison.OrdinalIgnoreCase))
        {
            return "ゴシック / サンセリフ";
        }

        if (familyName.Contains("行書", StringComparison.OrdinalIgnoreCase) ||
            familyName.Contains("草書", StringComparison.OrdinalIgnoreCase) ||
            familyName.Contains("楷書", StringComparison.OrdinalIgnoreCase) ||
            familyName.Contains("教科書", StringComparison.OrdinalIgnoreCase) ||
            familyName.Contains("筆", StringComparison.OrdinalIgnoreCase) ||
            familyName.Contains("Script", StringComparison.OrdinalIgnoreCase))
        {
            return "手書き / 筆記体";
        }

        if (familyName.Contains("Popup", StringComparison.OrdinalIgnoreCase) ||
            familyName.Contains("ポップ", StringComparison.OrdinalIgnoreCase))
        {
            return "装飾 / 見出し";
        }

        // 4. デザイン骨格が特定できなかった等幅フォントは「等幅」とする
        if (spacing == "等幅") return "等幅";

        return "不明";
    }

    public static string ClassifyFromPanose(OpenTypeReader otReader)
    {
        var panose = otReader.GetPanose();
        if (panose is null) return "不明";

        return panose[0] switch
        {
            2 => panose[1] switch
            {
                2 or 3 or 4 or 5 or 6 or 7 or 8 or 9 or 10 => "明朝 / セリフ",
                11 or 12 or 13 or 14 or 15 => "ゴシック / サンセリフ",
                _ => "不明"
            },
            3 => "手書き / 筆記体",
            4 => "装飾 / 見出し",
            5 => "記号 / シンボル",
            _ => "不明"
        };
    }

    public static bool CheckJisLevel1(OpenTypeReader otReader)
    {
        // JIS第1水準代表文字（亜、腕、鬱、漢、字）
        ReadOnlySpan<int> jis1Samples = [0x4E9C, 0x8155, 0x9B31, 0x6F22, 0x5B57];
        foreach (var cp in jis1Samples)
        {
            if (!otReader.HasGlyph(cp)) return false;
        }
        return true;
    }

    public static bool CheckJisLevel2(OpenTypeReader otReader)
    {
        // JIS第2水準代表文字（弌、丐、丒、熙、褞）
        ReadOnlySpan<int> jis2Samples = [0x5F01, 0x4E10, 0x4E12, 0x7195, 0x891E];
        int hit = 0;
        foreach (var cp in jis2Samples)
        {
            if (otReader.HasGlyph(cp)) hit++;
        }
        return hit >= 3;
    }

    public static bool CheckJisLevel3And4(OpenTypeReader otReader)
    {
        // JIS第3・第4水準代表文字（俱、剝、吞、髙、𠮷）
        ReadOnlySpan<int> jis34Samples = [0x4FF1, 0x525D, 0x541E, 0x9AD9, 0x20BB7];
        int hit = 0;
        foreach (var cp in jis34Samples)
        {
            if (otReader.HasGlyph(cp)) hit++;
        }
        return hit >= 3;
    }

    public static void RefineLanguages(TypefaceInfo info, OpenTypeReader otReader)
    {
        var cpRanges = otReader.GetCodePageRanges();
        bool? hasBig5 = null;
        bool? hasPrc = null;

        if (cpRanges is not null)
        {
            var (r1, _) = cpRanges.Value;
            hasBig5 = (r1 & (1u << 20)) != 0;
            hasPrc = (r1 & (1u << 18)) != 0;
        }

        // 繁体字の補正:
        // OS/2テーブルが存在し、Big5ビットが立っていないフォント（日本のJISフォント等）は除外
        var removeTc = info.Languages.Contains("中国語 (繁体)") && hasBig5 == false;

        // 簡体字の補正:
        // OS/2テーブルのPRCビットが立っていないフォントは除外。
        // また、和文フォント（かな文字収録）で中国語専用フォントでない場合（JS平成明朝体など）も除外
        var hasKana = info.Languages.Contains("日本語");
        var removeSc = info.Languages.Contains("中国語 (簡体)") &&
                       (hasPrc == false || (hasKana && hasPrc != true));

        if (removeTc || removeSc)
        {
            info.Languages = info.Languages
                .Where(lang => (!removeTc || lang != "中国語 (繁体)") && (!removeSc || lang != "中国語 (簡体)"))
                .ToList();
        }
    }
}
