using System;
using System.Collections.Generic;

namespace FontSelector.Services.OpenType;

public class CmapTableParser
{
    private readonly BigEndianReader _reader;
    private readonly uint _offset;

    private readonly object _cmapLock = new();
    private List<(uint start, uint end)>? _cmapRanges;
    private bool _cmapParsed;
    private bool _hasFormat14Cmap;

    public CmapTableParser(BigEndianReader reader, uint offset)
    {
        _reader = reader;
        _offset = offset;
    }

    public bool HasFormat14Cmap
    {
        get
        {
            EnsureCmapParsed();
            return _hasFormat14Cmap;
        }
    }

    public IReadOnlyList<(uint start, uint end)>? CmapRanges
    {
        get
        {
            EnsureCmapParsed();
            return _cmapRanges;
        }
    }

    public bool HasGlyph(int codePoint)
    {
        EnsureCmapParsed();

        if (_cmapRanges is null || _cmapRanges.Count == 0)
            return false;

        return _cmapRanges.ContainsCodePoint((uint)codePoint);
    }

    private void EnsureCmapParsed()
    {
        if (!_cmapParsed)
        {
            lock (_cmapLock)
            {
                if (!_cmapParsed)
                {
                    ParseCmap();
                    _cmapParsed = true;
                }
            }
        }
    }

    private void ParseCmap()
    {
        int cmapOffset = (int)_offset;
        if (cmapOffset + 4 > _reader.Length) return;

        var numSubtables = _reader.ReadUInt16(cmapOffset + 2);
        int subtableOffset12 = -1;
        int subtableOffset4 = -1;

        for (int i = 0; i < numSubtables; i++)
        {
            int recOffset = cmapOffset + 4 + i * 8;
            if (recOffset + 8 > _reader.Length) break;

            var offset = (int)_reader.ReadUInt32(recOffset + 4);
            int absOffset = cmapOffset + offset;
            if (absOffset + 2 > _reader.Length) continue;

            var format = _reader.ReadUInt16(absOffset);
            if (format == 14)
            {
                _hasFormat14Cmap = true;
            }
            else if (format == 12 && subtableOffset12 == -1)
            {
                subtableOffset12 = absOffset;
            }
            else if (format == 4 && subtableOffset4 == -1)
            {
                subtableOffset4 = absOffset;
            }
        }

        var rawRanges = new List<(uint start, uint end)>();

        // Format 12: 32 ビット全 Unicode (絵文字など)
        if (subtableOffset12 != -1 && subtableOffset12 + 16 <= _reader.Length)
        {
            var nGroups = _reader.ReadUInt32(subtableOffset12 + 12);
            int groupOffset = subtableOffset12 + 16;
            for (uint g = 0; g < nGroups; g++)
            {
                int currentOffset = groupOffset + (int)(g * 12);
                if (currentOffset + 8 > _reader.Length) break;

                var startChar = _reader.ReadUInt32(currentOffset);
                var endChar = _reader.ReadUInt32(currentOffset + 4);
                if (startChar <= endChar)
                {
                    rawRanges.Add((startChar, endChar));
                }
            }
        }

        // Format 4: 16 ビット BMP
        if (subtableOffset4 != -1 && subtableOffset4 + 14 <= _reader.Length)
        {
            var segCountX2 = _reader.ReadUInt16(subtableOffset4 + 6);
            int segCount = segCountX2 / 2;
            int endCodeOffset = subtableOffset4 + 14;
            int startCodeOffset = endCodeOffset + segCount * 2 + 2; // +2 for reservedPad

            if (startCodeOffset + segCount * 2 <= _reader.Length)
            {
                for (int s = 0; s < segCount; s++)
                {
                    var endCode = _reader.ReadUInt16(endCodeOffset + s * 2);
                    var startCode = _reader.ReadUInt16(startCodeOffset + s * 2);
                    if (startCode <= endCode && endCode != 0xFFFF)
                    {
                        rawRanges.Add((startCode, endCode));
                    }
                }
            }
        }

        if (rawRanges.Count == 0) return;

        // 区間をソートしてマージ
        rawRanges.Sort((a, b) => a.start.CompareTo(b.start));
        var merged = new List<(uint start, uint end)>(rawRanges.Count);
        var current = rawRanges[0];

        for (int i = 1; i < rawRanges.Count; i++)
        {
            var next = rawRanges[i];
            if (next.start <= current.end + 1)
            {
                current = (current.start, Math.Max(current.end, next.end));
            }
            else
            {
                merged.Add(current);
                current = next;
            }
        }
        merged.Add(current);

        _cmapRanges = merged;
    }
}
