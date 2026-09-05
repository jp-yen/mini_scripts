using System.Globalization;
using System.IO;
using System.Windows.Media;
using FontSelector.Models;

namespace FontSelector.Services;

/// <summary>
/// Retrieves installed system fonts and enriches each TypefaceInfo with
/// metadata from GlyphTypeface and OpenType table analysis.
/// </summary>
public class FontService : IFontService
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

    // Nerd Fonts PUA ranges to probe
    private static readonly int[] NerdFontProbeChars =
    [
        0xE0A0, // Powerline: branch
        0xE0B0, // Powerline: right triangle
        0xF001, // Font Awesome
        0xF0C5, // Font Awesome: copy
        0xE200, // Seti-UI
        0xF500, // Material Design
    ];

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

    // Cache OpenTypeReader per file path (many typefaces share the same file)
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, OpenTypeReader?> _otReaderCache = new();

    public IReadOnlyList<FontFamilyInfo> GetAllFonts(IProgress<(int current, int total, string fontName, int phase)>? progress = null)
    {
        // ──────────────────────────────────────────────────
        // Phase 1: WPF font enumeration (single-threaded)
        //   WPF の Fonts.SystemFontFamilies / GlyphTypeface は
        //   STA スレッドでのみ安全にアクセスできるため、ここで一括取得する。
        // ──────────────────────────────────────────────────
        var fontFamilies = Fonts.SystemFontFamilies.OrderBy(f => f.Source).ToList();
        var total = fontFamilies.Count;

        // Phase 1 で取得した「軽量」な中間データ
        var pendingItems = new List<(TypefaceInfo info, string filePath)>();
        var familyMap = new Dictionary<string, List<TypefaceInfo>>();
        var familyNameMap = new Dictionary<string, string>();

        for (int i = 0; i < total; i++)
        {
            var fontFamily = fontFamilies[i];
            
            // ── Extract Localized Family Name (prefer ja-jp over en-us) ──
            var names = fontFamily.FamilyNames;
            var jaLang = System.Windows.Markup.XmlLanguage.GetLanguage("ja-jp");
            var enLang = System.Windows.Markup.XmlLanguage.GetLanguage("en-us");
            
            string familyName = names.TryGetValue(jaLang, out var jaName) ? jaName
                              : names.TryGetValue(enLang, out var enName) ? enName
                              : names.Values.FirstOrDefault() ?? fontFamily.Source;

            familyNameMap[fontFamily.Source] = familyName;
            progress?.Report((i + 1, total, familyName, 1));

            var typefaces = new List<TypefaceInfo>();
            var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var typeface in fontFamily.GetTypefaces())
            {
                // 実体フォントファイルを持たない仮想フォント（Global Monospace 等）はスキップ
                if (!typeface.TryGetGlyphTypeface(out var glyphTypeface))
                    continue;

                // ── File path (WPF FontUri access) ──
                var filePath = GetFilePath(glyphTypeface);

                // 同一スタイル（Weight, Style, Stretch, ファイルパス）の重複を排除
                var key = $"{typeface.Weight.ToOpenTypeWeight()}|{typeface.Style}|{typeface.Stretch}|{filePath}";
                if (!seenKeys.Add(key))
                    continue;

                var info = new TypefaceInfo(fontFamily, familyName, typeface)
                {
                    // ── Basic metadata (GlyphTypeface から抽出し、GlyphTypeface 自体は保持しない) ──
                    Languages = DetectLanguages(glyphTypeface),
                    Spacing = DetectSpacing(glyphTypeface),
                    Vendor = GetVendor(glyphTypeface),
                    HasNerdFonts = DetectNerdFonts(glyphTypeface),
                    License = GetLicense(glyphTypeface),
                    Version = glyphTypeface.VersionStrings.Values.FirstOrDefault() ?? glyphTypeface.Version.ToString("F3"),
                    FilePath = filePath,
                    FileFormat = DetectFileFormat(filePath),
                    InstallType = DetectInstallType(filePath)
                };

                typefaces.Add(info);
                pendingItems.Add((info, filePath));
            }

            if (typefaces.Count > 0)
            {
                familyMap[fontFamily.Source] = typefaces;
            }
        }

        // ──────────────────────────────────────────────────
        // Phase 2: OpenType binary analysis (multi-threaded)
        //   ファイルI/Oとバイナリ解析が大部分を占めるため、
        //   Parallel.ForEach で CPU コアを活用して高速化する。
        // ──────────────────────────────────────────────────
        var processed = 0;
        var pendingTotal = pendingItems.Count;

        Parallel.ForEach(pendingItems,
            new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
            item =>
            {
                var (info, filePath) = item;

                var otReader = GetOpenTypeReader(filePath);
                if (otReader is not null)
                {
                    // グリフ判定用の文字区間リストを設定（同一ファイルでインスタンス共有）
                    info.CmapRanges = otReader.GetCmapRanges();

                    // ── 太さ (Weight) の補正 ──
                    // WPF は複合フォントや多書体ファミリーで Weight を正しくパースできず 1 などの異常値を返すことがあるため、
                    // OS/2 テーブルの usWeightClass (100〜900) から正しい太さを復元する
                    var os2Weight = otReader.GetWeightClass();
                    if (os2Weight >= 100 && info.Weight.ToOpenTypeWeight() < 100)
                    {
                        info.UpdateWeight(System.Windows.FontWeight.FromOpenTypeWeight(os2Weight));
                    }

                    info.IsVariableFont = otReader.IsVariableFont;
                    info.VariationAxes = otReader.GetVariationAxes();
                    info.Category = DetectCategory(otReader, info.FamilyName, info.Spacing);

                    var features = otReader.GetAllFeatures();
                    info.OpenTypeFeatures = features;
                    info.HasLigatures = features.Contains("liga") || features.Contains("calt");
                    info.HasSlashedZero = features.Contains("zero");
                    info.HasSmallCaps = features.Contains("smcp");
                    info.HasOldStyleNumerals = features.Contains("onum");
                    info.HasStylisticSets = features.Any(f => f.StartsWith("ss") && f.Length == 4 && char.IsDigit(f[2]));

                    var outline = otReader.HasTable("CFF ") || otReader.HasTable("CFF2") ? "PostScript (CFF)"
                                  : otReader.HasTable("glyf") ? "TrueType" : "不明";
                    if (otReader.HasTable("GSUB") || otReader.HasTable("GPOS"))
                    {
                        outline = "OpenType Layout / " + outline;
                    }
                    info.OutlineFormat = outline;

                    // ── 収録領域・文字種 ──
                    info.HasIvs = otReader.HasFormat14Cmap;
                    info.HasEmoji = otReader.HasEmojiGlyphs();
                    info.HasJisLevel1 = CheckJisLevel1(otReader);
                    info.HasJisLevel2 = info.HasJisLevel1 && CheckJisLevel2(otReader);
                    info.HasJisLevel3And4 = info.HasJisLevel2 && CheckJisLevel3And4(otReader);

                    // ── 言語判定の精密化 (OS/2 CodePageRanges / 日本語固有フォント除外) ──
                    RefineLanguages(info, otReader);
                }

                var current = Interlocked.Increment(ref processed);
                progress?.Report((current, pendingTotal, info.FamilyName, 2));
            });

        // ──────────────────────────────────────────────────
        // Phase 3: メモリ解放とガベージコレクション
        //   初期化が完了したため、OpenTypeReader のテーブル生バイナリおよび
        //   キャッシュ辞書をクリアし、初期化時に発生した一時ヒープを OS に返却する。
        // ──────────────────────────────────────────────────
        foreach (var reader in _otReaderCache.Values)
        {
            reader?.ReleaseTableBytes();
        }
        _otReaderCache.Clear();

        GC.Collect(2, GCCollectionMode.Aggressive, true, true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Aggressive, true, true);

        // Build final result preserving alphabetical order
        var result = new List<FontFamilyInfo>();
        foreach (var fontFamily in fontFamilies)
        {
            if (familyMap.TryGetValue(fontFamily.Source, out var typefaces))
            {
                var fName = familyNameMap.TryGetValue(fontFamily.Source, out var name) ? name : fontFamily.Source;

                var sortedTypefaces = typefaces
                    .OrderBy(t => t.Weight.ToOpenTypeWeight())
                    .ThenBy(t => FontFamilyInfo.GetStyleRank(t.Style))
                    .ThenBy(t => t.Stretch.ToOpenTypeStretch())
                    .ThenBy(t => t.StyleName, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var typefaceNames = sortedTypefaces
                    .Select(t => t.DisplayName)
                    .Distinct(StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
                var countStr = $"{typefaceNames.Count} 書体";
                var tooltipStr = $"【{fName} ({typefaceNames.Count} 書体)】\n" + string.Join("\n", typefaceNames);

                foreach (var t in typefaces)
                {
                    t.FamilyTypefaceCount = countStr;
                    t.FamilyTypefacesTooltip = tooltipStr;
                }

                result.Add(new FontFamilyInfo(fName, typefaces, fontFamily.Source));
            }
        }

        return result;
    }

    public Task<IReadOnlyList<FontFamilyInfo>> GetAllFontsAsync(
        IProgress<(int current, int total, string fontName, int phase)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return GetAllFonts(progress);
        }, cancellationToken);
    }

    // ═══════════════════════════════════════════════════════
    // Detection methods
    // ═══════════════════════════════════════════════════════

    private static List<string> DetectLanguages(GlyphTypeface glyphTypeface)
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

    private static string DetectSpacing(GlyphTypeface glyphTypeface)
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

    private static string GetVendor(GlyphTypeface glyphTypeface)
    {
        try
        {
            var names = glyphTypeface.ManufacturerNames;
            if (names.Count == 0) return string.Empty;
            if (names.TryGetValue(CultureInfo.GetCultureInfo("en-US"), out var enName))
                return enName;
            return names.Values.FirstOrDefault() ?? string.Empty;
        }
        catch { return string.Empty; }
    }

    private static bool DetectNerdFonts(GlyphTypeface glyphTypeface)
    {
        var map = glyphTypeface.CharacterToGlyphMap;
        // Consider it a Nerd Font if it has glyphs for at least 3 of the probe characters
        var hits = NerdFontProbeChars.Count(c => map.ContainsKey(c));
        return hits >= 3;
    }

    private static string GetLicense(GlyphTypeface glyphTypeface)
    {
        try
        {
            var descriptions = glyphTypeface.LicenseDescriptions;
            if (descriptions.Count == 0) return string.Empty;
            if (descriptions.TryGetValue(CultureInfo.GetCultureInfo("en-US"), out var enLicense))
                return enLicense;
            return descriptions.Values.FirstOrDefault() ?? string.Empty;
        }
        catch { return string.Empty; }
    }

    private static string GetFilePath(GlyphTypeface glyphTypeface)
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

    private static string DetectFileFormat(string filePath)
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

    private static string DetectInstallType(string filePath)
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
    private static string DetectCategory(OpenTypeReader otReader, string familyName, string spacing)
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

    private static string ClassifyFromPanose(OpenTypeReader otReader)
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

    private OpenTypeReader? GetOpenTypeReader(string filePath)
    {
        if (string.IsNullOrEmpty(filePath)) return null;

        return _otReaderCache.GetOrAdd(filePath, path => OpenTypeReader.TryOpen(path));
    }

    private static bool CheckJisLevel1(OpenTypeReader otReader)
    {
        // JIS第1水準代表文字（亜、腕、鬱、漢、字）
        ReadOnlySpan<int> jis1Samples = [0x4E9C, 0x8155, 0x9B31, 0x6F22, 0x5B57];
        foreach (var cp in jis1Samples)
        {
            if (!otReader.HasGlyph(cp)) return false;
        }
        return true;
    }

    private static bool CheckJisLevel2(OpenTypeReader otReader)
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

    private static bool CheckJisLevel3And4(OpenTypeReader otReader)
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

    private static void RefineLanguages(TypefaceInfo info, OpenTypeReader otReader)
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
