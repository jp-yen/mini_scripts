// Printer.cs - パケットおよび 16 進ダンプのフォーマッタ

using System;
using System.Net;
using System.Text;

namespace RadiusTest
{
    // ------------------------------------------------------------------
    // パケットおよび 16 進ダンプのフォーマッタ
    // ------------------------------------------------------------------
    public static class Printer
    {
        public static string FormatValue(byte type, byte[] val)
        {
            if (!Dict.Attrs.TryGetValue(type, out var def))
            {
                return "0x" + ToHex(val);
            }
            try
            {
                switch (def.DataType)
                {
                    case "string":
                        return $"\"{Encoding.UTF8.GetString(val)}\"";
                    case "ipv4":
                        if (val.Length == 4) return new IPAddress(val).ToString();
                        break;
                    case "ipv6":
                        if (val.Length == 16) return new IPAddress(val).ToString();
                        break;
                    case "integer":
                        if (val.Length == 4)
                        {
                            uint n = (uint)((val[0] << 24) | (val[1] << 16) | (val[2] << 8) | val[3]);
                            if (def.Values != null && def.Values.TryGetValue((int)n, out var name))
                                return $"{n} ({name})";
                            return n.ToString();
                        }
                        break;
                    case "vsa":
                        if (val.Length >= 6)
                        {
                            uint vendor = (uint)((val[0] << 24) | (val[1] << 16) | (val[2] << 8) | val[3]);
                            byte vtype = val[4];
                            int vlen = val[5];
                            bool validVlen = vlen >= 2 && vlen - 2 <= val.Length - 6;
                            int safeLen = Math.Max(0, Math.Min(vlen >= 2 ? vlen - 2 : val.Length - 6, val.Length - 6));
                            string vdata = "0x" + ToHex(val, 6, safeLen);
                            if (!validVlen) vdata += $" [警告: 異常長宣言 len={vlen}]";

                            if (vendor == 311)
                            {
                                string msName = Dict.GetMsSubName(vtype);
                                if ((vtype == 2 || vtype == 26) && safeLen > 0)
                                    vdata = $"\"{Encoding.ASCII.GetString(val, 6, safeLen)}\"" + (!validVlen ? " [異常長]" : "");
                                return $"Microsoft [{msName}]: {vdata}";
                            }
                            return $"ベンダー={vendor}, サブタイプ={vtype}, {vdata}";
                        }
                        else
                        {
                            return $"0x{ToHex(val)} [警告: 短小 VSA ({val.Length} バイト < 6 バイト)]";
                        }
                }
            }
            catch { }
            return "0x" + ToHex(val);
        }

        public static string ToHex(byte[]? b, int off = 0, int len = -1)
        {
            if (b == null || b.Length == 0) return "";
            if (off < 0 || off > b.Length) return "";
            if (len < 0 || off + len > b.Length) len = b.Length - off;
            if (len <= 0) return "";
            return Convert.ToHexString(b, off, len).ToLowerInvariant();
        }

        public static void HexDump(byte[] b, string indent = "    ")
        {
            for (int i = 0; i < b.Length; i += 16)
            {
                var sb = new StringBuilder();
                sb.Append(indent);
                sb.Append(i.ToString("x4")).Append("  ");
                for (int j = 0; j < 16; j++)
                {
                    if (i + j < b.Length) sb.Append(b[i + j].ToString("x2")).Append(' ');
                    else sb.Append("   ");
                    if (j == 7) sb.Append(' ');
                }
                sb.Append(" |");
                for (int j = 0; j < 16; j++)
                {
                    if (i + j < b.Length)
                    {
                        byte c = b[i + j];
                        sb.Append(c >= 32 && c <= 126 ? (char)c : '.');
                    }
                    else sb.Append(' ');
                }
                sb.Append('|');
                Out.Line(true, ConsoleColor.DarkGray, sb.ToString());
            }
        }

        public static event Action<RadiusPacket, string>? OnPacketInspected;

        public static void PrintPacket(RadiusPacket p, string label, bool showHex)
        {
            try { OnPacketInspected?.Invoke(p, label); } catch { }
            Out.Blank(true);
            Out.Line(true, ConsoleColor.Cyan, $"---- パケット詳細: {label} ({p.Raw.Length} バイト) ----");
            Out.Line(true, ConsoleColor.Gray, $"  コード         : {p.Code} ({Dict.CodeName(p.Code)})");
            Out.Line(true, ConsoleColor.Gray, $"  識別子 (ID)    : {p.Identifier}");
            Out.Line(true, ConsoleColor.Gray, $"  パケット長     : {p.Raw.Length}");
            Out.Line(true, ConsoleColor.Gray, $"  オーセンティケータ: {ToHex(p.Authenticator)}");
            Out.Line(true, ConsoleColor.Gray, $"  属性一覧 ({p.Attrs.Count}):");

            foreach (var a in p.Attrs)
            {
                string val = FormatValue(a.Type, a.Value);
                if (val.Length > 240) val = val.Substring(0, 240) + "...";
                Out.Line(true, ConsoleColor.Gray, $"    [{a.Type,3}] {Dict.Name(a.Type),-24} 長さ={a.Value.Length + 2,-3} {val}");
            }
            if (p.Malformed) Out.Line(true, ConsoleColor.Yellow, "    (属性リストのフォーマット異常 - パースを中断)");

            byte[] eap = p.Concat(79);
            if (eap.Length > 0)
                Out.Line(true, ConsoleColor.Magenta, $"  EAP ペイロード ({eap.Length} バイト連結)");

            if (showHex)
            {
                Out.Line(true, ConsoleColor.Gray, "  16進ダンプ:");
                HexDump(p.Raw);
            }
            Out.Blank(true);
        }
    }
}
