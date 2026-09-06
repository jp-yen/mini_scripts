using System;

namespace FontSelector.Services.OpenType;

public class Os2TableParser
{
    private readonly BigEndianReader _reader;
    private readonly uint _offset;

    public Os2TableParser(BigEndianReader reader, uint offset)
    {
        _reader = reader;
        _offset = offset;
    }

    public ushort GetWeightClass()
    {
        var pos = (int)_offset + 4;
        if (pos + 2 > _reader.Length) return 0;
        return _reader.ReadUInt16(pos);
    }

    public ushort GetWidthClass()
    {
        var pos = (int)_offset + 6;
        if (pos + 2 > _reader.Length) return 0;
        return _reader.ReadUInt16(pos);
    }

    public int GetFamilyClass()
    {
        var pos = (int)_offset + 30;
        if (pos + 2 > _reader.Length) return -1;
        return _reader.ReadInt16(pos);
    }

    public byte[]? GetPanose()
    {
        var pos = (int)_offset + 32;
        if (pos + 10 > _reader.Length) return null;
        return _reader.ReadBytes(pos, 10);
    }

    /// <summary>
    /// Returns (ulCodePageRange1, ulCodePageRange2) from OS/2 table if version >= 1.
    /// </summary>
    public (uint range1, uint range2)? GetCodePageRanges()
    {
        var pos = (int)_offset;
        if (pos + 86 > _reader.Length) return null;
        var version = _reader.ReadUInt16(pos);
        if (version < 1) return null;

        var r1 = _reader.ReadUInt32(pos + 78);
        var r2 = _reader.ReadUInt32(pos + 82);
        return (r1, r2);
    }
}
