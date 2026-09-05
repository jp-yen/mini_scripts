using System;
using System.Text;

namespace FontSelector.Services.OpenType;

/// <summary>
/// A helper struct for reading big-endian values from a byte array.
/// </summary>
public readonly struct BigEndianReader
{
    private readonly byte[] _data;

    public BigEndianReader(byte[] data)
    {
        _data = data;
    }

    public int Length => _data?.Length ?? 0;

    public ushort ReadUInt16(int offset)
    {
        if (offset + 2 > Length) return 0;
        return (ushort)((_data[offset] << 8) | _data[offset + 1]);
    }

    public short ReadInt16(int offset)
    {
        if (offset + 2 > Length) return 0;
        return (short)((_data[offset] << 8) | _data[offset + 1]);
    }

    public uint ReadUInt32(int offset)
    {
        if (offset + 4 > Length) return 0;
        return ((uint)_data[offset] << 24)
             | ((uint)_data[offset + 1] << 16)
             | ((uint)_data[offset + 2] << 8)
             | _data[offset + 3];
    }

    public double ReadFixed(int offset)
    {
        var high = (short)ReadUInt16(offset);
        var low = ReadUInt16(offset + 2);
        return high + (low / 65536.0);
    }

    public string ReadAsciiString(int offset, int length)
    {
        if (offset + length > Length) return string.Empty;
        return Encoding.ASCII.GetString(_data, offset, length);
    }

    public byte[] ReadBytes(int offset, int length)
    {
        if (offset + length > Length) return Array.Empty<byte>();
        var result = new byte[length];
        Array.Copy(_data, offset, result, 0, length);
        return result;
    }
}
