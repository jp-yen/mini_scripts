// RadiusPacket.cs - RADIUS パケット & 属性データ構造 (エンコード / デコード)

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace RadiusTest
{
    // ------------------------------------------------------------------
    // パケット & 属性データ構造
    // ------------------------------------------------------------------
    public class RadiusAttr
    {
        public byte Type;
        public byte[] Value;

        public RadiusAttr(byte t, byte[]? v)
        {
            Type = t;
            Value = v ?? Array.Empty<byte>();
        }

        public static RadiusAttr CreateVsa(uint vendorId, byte subType, byte[] subData)
        {
            ArgumentNullException.ThrowIfNull(subData);
            if (subData.Length > 247)
                throw new ArgumentException($"VSA サブデータの長さ ({subData.Length} バイト) が上限 (247 バイト) を超えています。", nameof(subData));

            byte[] v = new byte[6 + subData.Length];
            v[0] = (byte)(vendorId >> 24);
            v[1] = (byte)(vendorId >> 16);
            v[2] = (byte)(vendorId >> 8);
            v[3] = (byte)vendorId;
            v[4] = subType;
            v[5] = (byte)(2 + subData.Length);
            Buffer.BlockCopy(subData, 0, v, 6, subData.Length);
            return new RadiusAttr(26, v);
        }

        public (uint VendorId, byte SubType, byte[] SubData)? AsVsa()
        {
            if (Type != 26 || Value.Length < 6) return null;
            uint vid = (uint)((Value[0] << 24) | (Value[1] << 16) | (Value[2] << 8) | Value[3]);
            byte st = Value[4];
            int slen = Value[5];
            int available = Math.Max(0, Value.Length - 6);
            int dataLen = (slen >= 2) ? Math.Min(slen - 2, available) : available;
            byte[] sdata = new byte[dataLen];
            Buffer.BlockCopy(Value, 6, sdata, 0, dataLen);
            return (vid, st, sdata);
        }
    }

    public class RadiusPacket
    {
        public byte Code;
        public byte Identifier;
        public byte[] Authenticator = new byte[16];
        public List<RadiusAttr> Attrs = new List<RadiusAttr>();
        public byte[] Raw = Array.Empty<byte>();
        public bool Malformed;

        public byte[]? Get(byte type)
        {
            foreach (var a in Attrs) if (a.Type == type) return a.Value;
            return null;
        }

        public (byte SubType, byte[] SubData)? GetMsVsa(byte subType)
        {
            foreach (var a in Attrs)
            {
                var vsa = a.AsVsa();
                if (vsa.HasValue && vsa.Value.VendorId == 311 && vsa.Value.SubType == subType)
                    return (vsa.Value.SubType, vsa.Value.SubData);
            }
            return null;
        }

        public byte[] Concat(byte type)
        {
            int total = 0;
            foreach (var a in Attrs) if (a.Type == type) total += a.Value.Length;
            if (total == 0) return Array.Empty<byte>();
            byte[] res = new byte[total];
            int pos = 0;
            foreach (var a in Attrs)
            {
                if (a.Type == type)
                {
                    Buffer.BlockCopy(a.Value, 0, res, pos, a.Value.Length);
                    pos += a.Value.Length;
                }
            }
            return res;
        }

        public byte[] Encode(byte[]? secret = null)
        {
            if (Authenticator == null || Authenticator.Length != 16)
                throw new InvalidOperationException("Authenticator は 16 バイトである必要があります。");

            int len = 20;
            foreach (var a in Attrs)
            {
                if (a.Value.Length > 253)
                    throw new InvalidOperationException($"属性 Type {a.Type} のサイズ ({a.Value.Length} バイト) が RADIUS 属性上限 (253 バイト) を超えています。");
                len += 2 + a.Value.Length;
            }
            if (len > 4096) throw new InvalidOperationException($"パケットサイズが上限を超えています: {len} バイト (最大 4096 バイト)");

            byte[] buf = new byte[len];
            buf[0] = Code;
            buf[1] = Identifier;
            buf[2] = (byte)(len >> 8);
            buf[3] = (byte)len;
            Buffer.BlockCopy(Authenticator, 0, buf, 4, 16);

            int off = 20;
            int msgAuthOff = -1;
            foreach (var a in Attrs)
            {
                buf[off++] = a.Type;
                buf[off++] = (byte)(2 + a.Value.Length);
                if (a.Type == 80 && a.Value.Length == 16) msgAuthOff = off;
                Buffer.BlockCopy(a.Value, 0, buf, off, a.Value.Length);
                off += a.Value.Length;
            }

            if (msgAuthOff >= 0 && secret != null)
            {
                Array.Clear(buf, msgAuthOff, 16);
                using var hmac = new HMACMD5(secret);
                byte[] mac = hmac.ComputeHash(buf);
                Buffer.BlockCopy(mac, 0, buf, msgAuthOff, 16);
            }

            Raw = buf;
            return buf;
        }

        public static RadiusPacket Parse(byte[] buf, int offset, int length)
        {
            ArgumentNullException.ThrowIfNull(buf);
            if (offset < 0 || length < 0 || offset > buf.Length - length)
                throw new ArgumentOutOfRangeException(nameof(offset), "オフセットまたは長さがバッファの範囲外です。");
            if (length < 20) throw new InvalidDataException($"パケット長が短すぎます ({length} バイト、最小 20 バイト必要)");

            var p = new RadiusPacket
            {
                Code = buf[offset],
                Identifier = buf[offset + 1]
            };
            int pktLen = (buf[offset + 2] << 8) | buf[offset + 3];
            if (pktLen < 20 || pktLen > 4096 || pktLen > length)
            {
                p.Malformed = true;
                pktLen = length; // 異常値の場合もバッファ内データを保持して試験継続
            }

            Buffer.BlockCopy(buf, offset + 4, p.Authenticator, 0, 16);
            p.Raw = new byte[pktLen];
            Buffer.BlockCopy(buf, offset, p.Raw, 0, pktLen);

            int cur = offset + 20;
            int end = offset + pktLen;
            while (cur < end)
            {
                if (cur + 2 > end) { p.Malformed = true; break; }
                byte type = buf[cur];
                int attrLen = buf[cur + 1];
                if (attrLen < 2 || cur + attrLen > end)
                {
                    p.Malformed = true;
                    int remaining = end - (cur + 2);
                    if (remaining > 0)
                    {
                        byte[] rest = new byte[remaining];
                        Buffer.BlockCopy(buf, cur + 2, rest, 0, remaining);
                        p.Attrs.Add(new RadiusAttr(type, rest));
                    }
                    break;
                }
                byte[] val = new byte[attrLen - 2];
                Buffer.BlockCopy(buf, cur + 2, val, 0, val.Length);
                p.Attrs.Add(new RadiusAttr(type, val));
                cur += attrLen;
            }
            return p;
        }
    }
}
