// EapPacket.cs - EAP パケット構造体 (RFC 3748)

using System;
using System.Security.Cryptography;

namespace RadiusTest
{
    // ------------------------------------------------------------------
    // EAP パケット構造体 (RFC 3748)
    // ------------------------------------------------------------------
    public class EapPacket
    {
        public byte Code;        // 1=Request, 2=Response, 3=Success, 4=Failure
        public byte Identifier;
        public byte Type;        // 1=Identity, 6=GTC, 13=EAP-TLS, 21=EAP-TTLS, 25=PEAP, 26=MS-CHAPv2, 43=FAST, 55=TEAP
        public byte[] Data = Array.Empty<byte>();

        public static string CodeName(byte code) => code switch
        {
            1 => "Request",
            2 => "Response",
            3 => "Success",
            4 => "Failure",
            _ => $"Code-{code}"
        };

        public static string TypeName(byte type) => type switch
        {
            1 => "Identity",
            2 => "Notification",
            3 => "NAK",
            4 => "MD5-Challenge",
            6 => "EAP-GTC",
            13 => "EAP-TLS",
            21 => "EAP-TTLS",
            25 => "PEAP",
            26 => "EAP-MSCHAPv2",
            43 => "EAP-FAST",
            55 => "EAP-TEAP",
            _ => $"Type-{type}"
        };

        public byte[] Encode()
        {
            if (Code == 3 || Code == 4)
            {
                byte[] b = new byte[4];
                b[0] = Code;
                b[1] = Identifier;
                b[2] = 0;
                b[3] = 4;
                return b;
            }

            int len = 5 + (Data?.Length ?? 0);
            if (len > 65535)
                throw new InvalidOperationException($"EAP パケット長 ({len} バイト) がプロトコル上限 (65535 バイト) を超えています。");

            byte[] buf = new byte[len];
            buf[0] = Code;
            buf[1] = Identifier;
            buf[2] = (byte)(len >> 8);
            buf[3] = (byte)len;
            buf[4] = Type;
            if (Data != null && Data.Length > 0)
            {
                Buffer.BlockCopy(Data, 0, buf, 5, Data.Length);
            }
            return buf;
        }

        public static EapPacket? Parse(byte[]? buf, int offset, int length)
        {
            if (buf == null || offset < 0 || length < 4 || offset > buf.Length - length) return null;
            byte code = buf[offset];
            byte id = buf[offset + 1];
            int pktLen = (buf[offset + 2] << 8) | buf[offset + 3];
            int effectiveLen = Math.Min(pktLen, length);
            if (effectiveLen < 4) return null;

            var p = new EapPacket { Code = code, Identifier = id };
            if (code == 1 || code == 2)
            {
                if (effectiveLen >= 5)
                {
                    p.Type = buf[offset + 4];
                    p.Data = new byte[effectiveLen - 5];
                    Buffer.BlockCopy(buf, offset + 5, p.Data, 0, p.Data.Length);
                }
            }
            return p;
        }
    }
}
