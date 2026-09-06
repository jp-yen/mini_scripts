using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FontSelector.Models;
using FontSelector.Services.OpenType;

namespace FontSelector.Services;

/// <summary>
/// Lightweight OpenType font file parser.
/// Reads table directory and only requested tables (OS/2, fvar, GSUB, cmap) via stream
/// to avoid loading multi-megabyte font binaries into memory.
/// </summary>
public class OpenTypeReader
{
    private readonly string _filePath;
    private readonly Dictionary<string, (uint offset, uint length)> _tables = new(StringComparer.Ordinal);
    private readonly Dictionary<string, byte[]> _tableData = new(StringComparer.Ordinal);

    // Parsers (initialized on-demand)
    private Os2TableParser? _os2Parser;
    private FvarTableParser? _fvarParser;
    private CmapTableParser? _cmapParser;

    private OpenTypeReader(string filePath)
    {
        _filePath = filePath;
    }

    /// <summary>
    /// Try to create an OpenTypeReader from a font file path.
    /// Reads only the table directory from disk (~500 bytes), deferring table reading until needed.
    /// For .ttc files, reads the first font in the collection.
    /// </summary>
    public static OpenTypeReader? TryOpen(string filePath)
    {
        try
        {
            if (!File.Exists(filePath)) return null;

            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096);
            if (stream.Length < 12) return null;

            var reader = new OpenTypeReader(filePath);
            if (!reader.ParseTableDirectory(stream)) return null;

            return reader._tables.Count > 0 ? reader : null;
        }
        catch
        {
            return null;
        }
    }

    private bool ParseTableDirectory(Stream stream)
    {
        Span<byte> header = stackalloc byte[12];
        if (stream.Read(header) < 12) return false;

        uint sfVersion = ((uint)header[0] << 24) | ((uint)header[1] << 16) | ((uint)header[2] << 8) | header[3];
        int tableStart = 12;

        // TTC (TrueType Collection) — read first font's OffsetTable
        if (sfVersion == 0x74746366) // "ttcf"
        {
            if (stream.Length < 16) return false;
            Span<byte> ttcBuf = stackalloc byte[4];
            stream.Position = 12;
            if (stream.Read(ttcBuf) < 4) return false;

            uint font0Offset = ((uint)ttcBuf[0] << 24) | ((uint)ttcBuf[1] << 16) | ((uint)ttcBuf[2] << 8) | ttcBuf[3];
            if (font0Offset + 12 > stream.Length) return false;

            stream.Position = font0Offset;
            if (stream.Read(header) < 12) return false;
            tableStart = (int)font0Offset + 12;
        }

        ushort numTables = (ushort)((header[4] << 8) | header[5]);
        if (numTables == 0 || numTables > 200) return false;

        int dirLen = numTables * 16;
        if (tableStart + dirLen > stream.Length) return false;

        byte[] dirBytes = new byte[dirLen];
        stream.Position = tableStart;
        if (stream.Read(dirBytes, 0, dirLen) < dirLen) return false;

        for (int i = 0; i < numTables; i++)
        {
            int entryOffset = i * 16;
            string tag = Encoding.ASCII.GetString(dirBytes, entryOffset, 4);
            uint tblOffset = ((uint)dirBytes[entryOffset + 8] << 24)
                           | ((uint)dirBytes[entryOffset + 9] << 16)
                           | ((uint)dirBytes[entryOffset + 10] << 8)
                           | dirBytes[entryOffset + 11];
            uint tblLength = ((uint)dirBytes[entryOffset + 12] << 24)
                           | ((uint)dirBytes[entryOffset + 13] << 16)
                           | ((uint)dirBytes[entryOffset + 14] << 8)
                           | dirBytes[entryOffset + 15];

            _tables[tag] = (tblOffset, tblLength);
        }

        return _tables.Count > 0;
    }

    private byte[]? GetTableBytes(string tag)
    {
        lock (_tableData)
        {
            if (_tableData.TryGetValue(tag, out var cached))
                return cached;

            if (!_tables.TryGetValue(tag, out var tbl) || tbl.length == 0 || tbl.length > 10_000_000)
                return null;

            try
            {
                using var stream = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096);
                if (tbl.offset + tbl.length > stream.Length) return null;

                stream.Position = tbl.offset;
                var buffer = new byte[tbl.length];
                var read = stream.Read(buffer, 0, (int)tbl.length);
                if (read == tbl.length)
                {
                    _tableData[tag] = buffer;
                    return buffer;
                }
            }
            catch
            {
                // Swallow read errors
            }

            return null;
        }
    }

    /// <summary>
    /// メモリ節約のため、キャッシュされたテーブル生バイナリ（byte[]）を解放します。
    /// 解析が完了した後に呼び出すことでメモリ消費を大幅に抑制できます。
    /// </summary>
    public void ReleaseTableBytes()
    {
        lock (_tableData)
        {
            _tableData.Clear();
            _os2Parser = null;
            _fvarParser = null;
            _cmapParser = null;
        }
    }

    // ═══════════════════════════════════════════════════════
    // Public query methods
    // ═══════════════════════════════════════════════════════

    /// <summary>Check if a specific table exists in the font.</summary>
    public bool HasTable(string tag) => _tables.ContainsKey(tag);

    /// <summary>True if the font contains an 'fvar' table (Variable Font).</summary>
    public bool IsVariableFont => HasTable("fvar");

    /// <summary>
    /// フォントがカラーフォントテーブル (COLR, CBDT, sbix, SVG ) を保持しているか判定します。
    /// </summary>
    public bool HasColorTable => HasTable("COLR") || HasTable("CBDT") || HasTable("sbix") || HasTable("SVG ");

    public ushort GetWeightClass()
    {
        EnsureOs2Parser();
        return _os2Parser?.GetWeightClass() ?? 0;
    }

    public ushort GetWidthClass()
    {
        EnsureOs2Parser();
        return _os2Parser?.GetWidthClass() ?? 0;
    }

    /// <summary>
    /// Read the sFamilyClass field from the OS/2 table.
    /// Returns the class ID (high byte = class, low byte = subclass), or -1 if unavailable.
    /// </summary>
    public int GetFamilyClass()
    {
        EnsureOs2Parser();
        return _os2Parser?.GetFamilyClass() ?? -1;
    }

    /// <summary>
    /// Read the Panose classification from the OS/2 table.
    /// Returns a 10-byte array, or null if unavailable.
    /// </summary>
    public byte[]? GetPanose()
    {
        EnsureOs2Parser();
        return _os2Parser?.GetPanose();
    }

    public (uint range1, uint range2)? GetCodePageRanges()
    {
        EnsureOs2Parser();
        return _os2Parser?.GetCodePageRanges();
    }

    private void EnsureOs2Parser()
    {
        if (_os2Parser is not null) return;
        lock (_tableData)
        {
            if (_os2Parser is not null) return;
            var bytes = GetTableBytes("OS/2");
            if (bytes is not null)
            {
                _os2Parser = new Os2TableParser(new BigEndianReader(bytes), 0);
            }
        }
    }

    /// <summary>
    /// Get all OpenType feature tags from the GSUB table.
    /// </summary>
    public HashSet<string> GetGsubFeatures()
    {
        var bytes = GetTableBytes("GSUB");
        if (bytes is null) return new HashSet<string>();
        var parser = new GsubGposTableParser(new BigEndianReader(bytes));
        return parser.GetFeatures(0);
    }

    /// <summary>
    /// Get all OpenType feature tags from the GPOS table.
    /// </summary>
    public HashSet<string> GetGposFeatures()
    {
        var bytes = GetTableBytes("GPOS");
        if (bytes is null) return new HashSet<string>();
        var parser = new GsubGposTableParser(new BigEndianReader(bytes));
        return parser.GetFeatures(0);
    }

    /// <summary>
    /// Get combined feature tags from both GSUB and GPOS tables.
    /// </summary>
    public HashSet<string> GetAllFeatures()
    {
        var features = GetGsubFeatures();
        features.UnionWith(GetGposFeatures());
        return features;
    }

    /// <summary>
    /// fvar テーブルからバリアブルフォントの可変軸リストを取得
    /// </summary>
    public List<VariationAxis> GetVariationAxes()
    {
        if (_fvarParser is null)
        {
            lock (_tableData)
            {
                if (_fvarParser is null)
                {
                    var bytes = GetTableBytes("fvar");
                    if (bytes is not null)
                    {
                        _fvarParser = new FvarTableParser(new BigEndianReader(bytes), 0);
                    }
                }
            }
        }
        return _fvarParser?.GetVariationAxes() ?? new List<VariationAxis>();
    }

    /// <summary>
    /// フォントが異体字セレクタ (IVS / cmap Format 14) テーブルを保持しているか判定します。
    /// </summary>
    public bool HasFormat14Cmap
    {
        get
        {
            EnsureCmapParser();
            return _cmapParser?.HasFormat14Cmap ?? false;
        }
    }

    /// <summary>
    /// フォントが指定された Unicode コードポイント（絵文字などの 32 ビットコードポイントを含む）を収録しているか判定します。
    /// </summary>
    public bool HasGlyph(int codePoint)
    {
        EnsureCmapParser();
        return _cmapParser?.HasGlyph(codePoint) ?? false;
    }

    /// <summary>
    /// cmap テーブルから解析された収録文字の区間リストを取得します。
    /// </summary>
    public IReadOnlyList<(uint start, uint end)>? GetCmapRanges()
    {
        EnsureCmapParser();
        return _cmapParser?.CmapRanges;
    }

    private void EnsureCmapParser()
    {
        if (_cmapParser is not null) return;
        lock (_tableData)
        {
            if (_cmapParser is not null) return;
            var bytes = GetTableBytes("cmap");
            if (bytes is not null)
            {
                _cmapParser = new CmapTableParser(new BigEndianReader(bytes), 0);
            }
        }
    }

    /// <summary>
    /// フォントが絵文字を主要に収録しているか判定します。
    /// </summary>
    public bool HasEmojiGlyphs()
    {
        if (HasColorTable) return true;

        ReadOnlySpan<int> emojiSamples =
        [
            0x1F600, 0x1F602, 0x1F60A, 0x1F613, // 顔
            0x1F44D, 0x1F44F,                   // 手
            0x1F34E, 0x1F35C,                   // 食べ物
            0x1F697, 0x1F436                    // 乗り物・動物
        ];

        int hitCount = 0;
        foreach (var cp in emojiSamples)
        {
            if (HasGlyph(cp))
            {
                hitCount++;
                if (hitCount >= 3) return true;
            }
        }

        return false;
    }
}
