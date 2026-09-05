using System;
using System.Collections.Generic;
using FontSelector.Models;

namespace FontSelector.Services.OpenType;

public class FvarTableParser
{
    private readonly BigEndianReader _reader;
    private readonly uint _offset;

    public FvarTableParser(BigEndianReader reader, uint offset)
    {
        _reader = reader;
        _offset = offset;
    }

    public List<VariationAxis> GetVariationAxes()
    {
        var list = new List<VariationAxis>();
        try
        {
            var start = (int)_offset;
            if (start + 16 > _reader.Length) return list;

            var axesOffset = _reader.ReadUInt16(start + 4);
            var axisCount = _reader.ReadUInt16(start + 8);
            var axisSize = _reader.ReadUInt16(start + 10);

            var recordStart = start + axesOffset;
            for (int i = 0; i < axisCount; i++)
            {
                var offset = recordStart + i * axisSize;
                if (offset + 16 > _reader.Length) break;

                var tag = _reader.ReadAsciiString(offset, 4);
                var min = _reader.ReadFixed(offset + 4);
                var def = _reader.ReadFixed(offset + 8);
                var max = _reader.ReadFixed(offset + 12);

                var name = tag switch
                {
                    "wght" => "太さ (Weight)",
                    "wdth" => "文字幅 (Width)",
                    "slnt" => "傾き (Slant)",
                    "ital" => "イタリック (Italic)",
                    "opsz" => "光学サイズ (Optical Size)",
                    _ => tag
                };

                list.Add(new VariationAxis
                {
                    Tag = tag,
                    Name = name,
                    MinValue = min,
                    DefaultValue = def,
                    MaxValue = max
                });
            }
        }
        catch
        {
            // 解析エラーは無視
        }

        return list;
    }
}
