// EapSession.Tlv.cs - FAST / TEAP / TTLS 共通の TLV・AVP ヘルパー

using System;
using System.Collections.Generic;
using System.IO;

namespace RadiusTest
{
    public partial class EapSession
    {
        // --------------------------------------------------------------
        // EAP-FAST / EAP-TEAP / EAP-TTLS 共通の TLV・AVP ヘルパー
        // --------------------------------------------------------------
        public class FastTlvItem
        {
            public bool Mandatory;
            public ushort Type;
            public byte[] Value = Array.Empty<byte>();
        }

        private static List<FastTlvItem> ParseFastTlvs(byte[]? chunk)
        {
            var list = new List<FastTlvItem>();
            if (chunk == null) return list;
            int offset = 0;
            while (offset + 4 <= chunk.Length)
            {
                ushort typeField = (ushort)((chunk[offset] << 8) | chunk[offset + 1]);
                bool mandatory = (typeField & 0x8000) != 0;
                ushort tlvType = (ushort)(typeField & 0x3FFF);
                ushort len = (ushort)((chunk[offset + 2] << 8) | chunk[offset + 3]);
                offset += 4;
                if (len > chunk.Length - offset)
                {
                    // TLV 長フィールドがバッファ残量を超過している不正データは安全に処理を終了
                    break;
                }
                byte[] val = new byte[len];
                Buffer.BlockCopy(chunk, offset, val, 0, len);
                offset += len;
                list.Add(new FastTlvItem { Mandatory = mandatory, Type = tlvType, Value = val });
            }
            return list;
        }

        private static void WriteFastTlv(Stream s, ushort type, byte[]? val, bool mandatory = true)
        {
            ArgumentNullException.ThrowIfNull(s);
            val ??= Array.Empty<byte>();
            if (val.Length > 65535)
                throw new InvalidOperationException($"TLV 値サイズ ({val.Length} バイト) が 16bit 上限を超えています。");

            ushort typeField = (ushort)(type | (mandatory ? 0x8000 : 0x0000));
            s.WriteByte((byte)(typeField >> 8));
            s.WriteByte((byte)typeField);
            s.WriteByte((byte)(val.Length >> 8));
            s.WriteByte((byte)val.Length);
            s.Write(val, 0, val.Length);
        }

        private static byte[] CreateAvp(uint code, byte[] data, bool mandatory = true)
        {
            int avpLen = 8 + data.Length;
            int pad = (4 - (avpLen % 4)) % 4;
            byte[] b = new byte[avpLen + pad];
            b[0] = (byte)(code >> 24);
            b[1] = (byte)(code >> 16);
            b[2] = (byte)(code >> 8);
            b[3] = (byte)code;
            b[4] = mandatory ? (byte)0x40 : (byte)0x00;
            b[5] = (byte)(avpLen >> 16);
            b[6] = (byte)(avpLen >> 8);
            b[7] = (byte)avpLen;
            Buffer.BlockCopy(data, 0, b, 8, data.Length);
            return b;
        }

        private static byte[] CreateVendorAvp(uint vendorId, uint code, byte[] data, bool mandatory = false)
        {
            int avpLen = 12 + data.Length;
            int pad = (4 - (avpLen % 4)) % 4;
            byte[] b = new byte[avpLen + pad];
            b[0] = (byte)(code >> 24);
            b[1] = (byte)(code >> 16);
            b[2] = (byte)(code >> 8);
            b[3] = (byte)code;
            b[4] = (byte)(0x80 | (mandatory ? 0x40 : 0x00)); // V-bit = 1 (Vendor-Specific)
            b[5] = (byte)(avpLen >> 16);
            b[6] = (byte)(avpLen >> 8);
            b[7] = (byte)avpLen;
            b[8] = (byte)(vendorId >> 24);
            b[9] = (byte)(vendorId >> 16);
            b[10] = (byte)(vendorId >> 8);
            b[11] = (byte)vendorId;
            Buffer.BlockCopy(data, 0, b, 12, data.Length);
            return b;
        }
    }
}
