using System;
using System.Collections.Generic;

namespace FontSelector.Services.OpenType;

public class GsubGposTableParser
{
    private readonly BigEndianReader _reader;

    public GsubGposTableParser(BigEndianReader reader)
    {
        _reader = reader;
    }

    public HashSet<string> GetFeatures(uint tableOffset)
    {
        var features = new HashSet<string>();
        try
        {
            var start = (int)tableOffset;
            if (start + 10 > _reader.Length) return features;

            // GSUB/GPOS Header: Version (4), ScriptListOffset (2), FeatureListOffset (2), LookupListOffset (2)
            var majorVersion = _reader.ReadUInt16(start);
            var featureListOffset = _reader.ReadUInt16(start + 6);

            var featureListStart = start + featureListOffset;
            if (featureListStart + 2 > _reader.Length) return features;

            var featureCount = _reader.ReadUInt16(featureListStart);

            for (int i = 0; i < featureCount; i++)
            {
                var recordOffset = featureListStart + 2 + i * 6;
                if (recordOffset + 4 > _reader.Length) break;

                var tag = _reader.ReadAsciiString(recordOffset, 4).Trim('\0');
                features.Add(tag);
            }
        }
        catch
        {
            // Swallow parse errors
        }

        return features;
    }
}
