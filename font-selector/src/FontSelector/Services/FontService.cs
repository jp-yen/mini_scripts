using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using FontSelector.Models;

namespace FontSelector.Services;

/// <summary>
/// Retrieves installed system fonts and enriches each TypefaceInfo with
/// metadata from GlyphTypeface and OpenType table analysis.
/// </summary>
public class FontService : IFontService
{
    // Cache OpenTypeReader per file path (many typefaces share the same file)
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, OpenTypeReader?> _otReaderCache = new();

    public IReadOnlyList<FontFamilyInfo> GetAllFonts(IProgress<(int current, int total, string fontName, int phase)>? progress = null)
    {
        return GetAllFontsAsync(progress).GetAwaiter().GetResult();
    }

    public async Task<IReadOnlyList<FontFamilyInfo>> GetAllFontsAsync(
        IProgress<(int current, int total, string fontName, int phase)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            // ──────────────────────────────────────────────────
            // Phase 1: WPF font enumeration & 30ファイルごとの解析スレッド投入
            // ──────────────────────────────────────────────────
            var fontFamilies = Fonts.SystemFontFamilies.OrderBy(f => f.Source).ToList();
            var totalFamilies = fontFamilies.Count;
            var totalTypefaces = fontFamilies.Sum(f => f.GetTypefaces().Count);

            var familyMap = new Dictionary<string, List<TypefaceInfo>>();
            var familyNameMap = new Dictionary<string, string>();
            var batchTasks = new List<Task>();
            const int BatchSize = 30;
            var currentBatch = new List<(TypefaceInfo info, string filePath)>();
            var processedTypefaceCount = 0;

            for (int i = 0; i < totalFamilies; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var fontFamily = fontFamilies[i];

                // ── Extract Localized Family Name (prefer ja-jp over en-us) ──
                var names = fontFamily.FamilyNames;
                var jaLang = System.Windows.Markup.XmlLanguage.GetLanguage("ja-jp");
                var enLang = System.Windows.Markup.XmlLanguage.GetLanguage("en-us");

                string familyName = names.TryGetValue(jaLang, out var jaName) ? jaName
                                  : names.TryGetValue(enLang, out var enName) ? enName
                                  : names.Values.FirstOrDefault() ?? fontFamily.Source;

                familyNameMap[fontFamily.Source] = familyName;
                progress?.Report((i + 1, totalFamilies, familyName, 1));

                var typefaces = new List<TypefaceInfo>();
                var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var typeface in fontFamily.GetTypefaces())
                {
                    // 実体フォントファイルを持たない仮想フォント（Global Monospace 等）はスキップ
                    if (!typeface.TryGetGlyphTypeface(out var glyphTypeface))
                        continue;

                    // ── File path (WPF FontUri access) ──
                    var filePath = FontClassifier.GetFilePath(glyphTypeface);

                    // 同一スタイル（Weight, Style, Stretch, ファイルパス）の重複を排除
                    var key = $"{typeface.Weight.ToOpenTypeWeight()}|{typeface.Style}|{typeface.Stretch}|{filePath}";
                    if (!seenKeys.Add(key))
                        continue;

                    var (cleanVersion, rawVersion) = FontClassifier.GetVersion(glyphTypeface, familyName);

                    var info = new TypefaceInfo(fontFamily, familyName, typeface)
                    {
                        // ── Basic metadata (GlyphTypeface から抽出し、GlyphTypeface 自体は保持しない) ──
                        Languages = FontClassifier.DetectLanguages(glyphTypeface),
                        Spacing = FontClassifier.DetectSpacing(glyphTypeface),
                        Vendor = FontClassifier.GetVendor(glyphTypeface),
                        HasNerdFonts = FontClassifier.DetectNerdFonts(glyphTypeface),
                        Version = cleanVersion,
                        RawVersion = rawVersion,
                        FilePath = filePath,
                        FileFormat = FontClassifier.DetectFileFormat(filePath),
                        InstallType = FontClassifier.DetectInstallType(filePath)
                    };

                    typefaces.Add(info);
                    currentBatch.Add((info, filePath));
                }

                if (typefaces.Count > 0)
                {
                    familyMap[fontFamily.Source] = typefaces;
                }

                // 30ファイル読み込むごとにバックグラウンド解析スレッドへ投入
                if (currentBatch.Count >= BatchSize)
                {
                    var batchToRun = currentBatch;
                    currentBatch = new List<(TypefaceInfo info, string filePath)>();
                    batchTasks.Add(Task.Run(() => ProcessTypefaceBatch(batchToRun, fontName =>
                    {
                        var cur = Interlocked.Increment(ref processedTypefaceCount);
                        progress?.Report((cur, totalTypefaces, fontName, 2));
                    }, cancellationToken), cancellationToken));
                }
            }

            // 残りのバッチがあれば投入
            if (currentBatch.Count > 0)
            {
                var batchToRun = currentBatch;
                batchTasks.Add(Task.Run(() => ProcessTypefaceBatch(batchToRun, fontName =>
                {
                    var cur = Interlocked.Increment(ref processedTypefaceCount);
                    progress?.Report((cur, totalTypefaces, fontName, 2));
                }, cancellationToken), cancellationToken));
            }

            // 全ての解析スレッドが完了するのを待機
            await Task.WhenAll(batchTasks);

            // ──────────────────────────────────────────────────
            // Phase 3: メモリ解放とガベージコレクション
            // ──────────────────────────────────────────────────
            foreach (var reader in _otReaderCache.Values)
            {
                reader?.ReleaseTableBytes();
            }
            _otReaderCache.Clear();

            GC.Collect(2, GCCollectionMode.Aggressive, true, true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Aggressive, true, true);

            // ──────────────────────────────────────────────────
            // 全処理終了後、フォントをソート
            // ──────────────────────────────────────────────────
            var result = new List<FontFamilyInfo>();
            foreach (var fontFamily in fontFamilies)
            {
                if (familyMap.TryGetValue(fontFamily.Source, out var typefaces))
                {
                    var resolvedFamilyName = familyNameMap.TryGetValue(fontFamily.Source, out var name) ? name : fontFamily.Source;

                    var sortedTypefaces = typefaces
                        .OrderBy(typeface => typeface.Weight.ToOpenTypeWeight())
                        .ThenBy(typeface => FontFamilyInfo.GetStyleRank(typeface.Style))
                        .ThenBy(typeface => typeface.Stretch.ToOpenTypeStretch())
                        .ThenBy(typeface => typeface.StyleName, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    result.Add(new FontFamilyInfo(resolvedFamilyName, sortedTypefaces, fontFamily.Source));
                }
            }

            return result.OrderBy(f => f.FamilyName, StringComparer.CurrentCultureIgnoreCase).ToList();
        }, cancellationToken);
    }

    private void ProcessTypefaceBatch(
        List<(TypefaceInfo info, string filePath)> batch,
        Action<string> onProcessed,
        CancellationToken cancellationToken)
    {
        foreach (var (info, filePath) in batch)
        {
            if (cancellationToken.IsCancellationRequested) break;
            ProcessTypefaceItem(info, filePath);
            onProcessed(info.FamilyName);
        }
    }

    private void ProcessTypefaceItem(TypefaceInfo info, string filePath)
    {
        var otReader = GetOpenTypeReader(filePath);
        if (otReader is not null)
        {
            // グリフ判定用の文字区間リストを設定（同一ファイルでインスタンス共有）
            info.CmapRanges = otReader.GetCmapRanges();

            // ── 太さ (Weight) の補正 ──
            var os2Weight = otReader.GetWeightClass();
            if (os2Weight >= 100 && info.Weight.ToOpenTypeWeight() < 100)
            {
                info.UpdateWeight(System.Windows.FontWeight.FromOpenTypeWeight(os2Weight));
            }

            info.IsVariableFont = otReader.IsVariableFont;
            info.VariationAxes = otReader.GetVariationAxes();
            info.Category = FontClassifier.DetectCategory(otReader, info.FamilyName, info.Spacing);

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
            info.HasJisLevel1 = FontClassifier.CheckJisLevel1(otReader);
            info.HasJisLevel2 = info.HasJisLevel1 && FontClassifier.CheckJisLevel2(otReader);
            info.HasJisLevel3And4 = info.HasJisLevel2 && FontClassifier.CheckJisLevel3And4(otReader);

            // ── 言語判定の精密化 (OS/2 CodePageRanges / 日本語固有フォント除外) ──
            FontClassifier.RefineLanguages(info, otReader);
        }
    }

    private OpenTypeReader? GetOpenTypeReader(string filePath)
    {
        if (string.IsNullOrEmpty(filePath)) return null;

        return _otReaderCache.GetOrAdd(filePath, path => OpenTypeReader.TryOpen(path));
    }
}
