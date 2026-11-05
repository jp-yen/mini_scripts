// RadiusDictionary.cs - RADIUS 辞書 & Microsoft VSA サブタイプ定義

using System;
using System.Collections.Generic;

namespace RadiusTest
{
    // ------------------------------------------------------------------
    // RADIUS 辞書 & Microsoft VSA サブタイプ定義
    // ------------------------------------------------------------------
    public class AttrDef
    {
        public byte Type;
        public string Name;
        public string DataType;
        public Dictionary<int, string>? Values;

        public AttrDef(byte t, string n, string d, Dictionary<int, string>? v = null)
        {
            Type = t; Name = n; DataType = d; Values = v;
        }
    }

    public static class Dict
    {
        public static readonly Dictionary<byte, AttrDef> Attrs = new Dictionary<byte, AttrDef>();
        public static readonly Dictionary<string, byte> Names = new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase);

        static Dict()
        {
            var st = new Dictionary<int, string> {
                {1,"Login-User"}, {2,"Framed"}, {3,"Callback-Login-User"},
                {4,"Callback-Framed"}, {5,"Outbound"}, {6,"Administrative"},
                {7,"NAS-Prompt"}, {8,"Authenticate-Only"}, {9,"Callback-NAS-Prompt"}
            };
            var ft = new Dictionary<int, string> {
                {1,"PPP"}, {2,"SLIP"}, {3,"ARAP"}, {4,"Gandalf"}, {5,"Xylogics"}, {6,"X.75"}
            };
            var stt = new Dictionary<int, string> {
                {1,"Start"}, {2,"Stop"}, {3,"Interim-Update"}, {7,"Accounting-On"}, {8,"Accounting-Off"}
            };
            var term = new Dictionary<int, string> {
                {1,"User-Request"}, {2,"Lost-Carrier"}, {4,"Idle-Timeout"}, {5,"Session-Timeout"},
                {6,"Admin-Reset"}, {7,"Admin-Reboot"}, {9,"NAS-Error"}, {10,"NAS-Request"}
            };

            Def(1, "User-Name", "string");
            Def(2, "User-Password", "binary");
            Def(3, "CHAP-Password", "binary");
            Def(4, "NAS-IP-Address", "ipv4");
            Def(5, "NAS-Port", "integer");
            Def(6, "Service-Type", "integer", st);
            Def(7, "Framed-Protocol", "integer", ft);
            Def(8, "Framed-IP-Address", "ipv4");
            Def(9, "Framed-IP-Netmask", "ipv4");
            Def(11, "Filter-Id", "string");
            Def(12, "Framed-MTU", "integer");
            Def(18, "Reply-Message", "string");
            Def(19, "Callback-Number", "string");
            Def(24, "State", "binary");
            Def(25, "Class", "binary");
            Def(26, "Vendor-Specific", "vsa");
            Def(27, "Session-Timeout", "integer");
            Def(28, "Idle-Timeout", "integer");
            Def(29, "Termination-Action", "integer");
            Def(30, "Called-Station-Id", "string");
            Def(31, "Calling-Station-Id", "string");
            Def(32, "NAS-Identifier", "string");
            Def(40, "Acct-Status-Type", "integer", stt);
            Def(41, "Acct-Delay-Time", "integer");
            Def(42, "Acct-Input-Octets", "integer");
            Def(43, "Acct-Output-Octets", "integer");
            Def(44, "Acct-Session-Id", "string");
            Def(49, "Acct-Terminate-Cause", "integer", term);
            Def(60, "CHAP-Challenge", "binary");
            Def(61, "NAS-Port-Type", "integer");
            Def(79, "EAP-Message", "binary");
            Def(80, "Message-Authenticator", "binary");
            Def(95, "NAS-IPv6-Address", "ipv6");
            Def(96, "Framed-Interface-Id", "binary");
            Def(97, "Framed-IPv6-Prefix", "binary");
        }

        private static void Def(byte t, string n, string d, Dictionary<int, string>? v = null)
        {
            var ad = new AttrDef(t, n, d, v);
            Attrs[t] = ad;
            Names[n] = t;
        }

        public static string Name(byte t) => Attrs.TryGetValue(t, out var a) ? a.Name : $"Attr-{t}";

        public static byte? Lookup(string nameOrNum)
        {
            if (byte.TryParse(nameOrNum, out byte n)) return n;
            if (Names.TryGetValue(nameOrNum, out byte b)) return b;
            return null;
        }

        public static string CodeName(byte c) => c switch
        {
            1 => "Access-Request",
            2 => "Access-Accept",
            3 => "Access-Reject",
            4 => "Accounting-Request",
            5 => "Accounting-Response",
            11 => "Access-Challenge",
            12 => "Status-Server",
            13 => "Status-Client",
            40 => "Disconnect-Request",
            41 => "Disconnect-ACK",
            42 => "Disconnect-NAK",
            43 => "CoA-Request",
            44 => "CoA-ACK",
            45 => "CoA-NAK",
            _ => $"Code-{c}"
        };

        public static string GetMsSubName(byte subType) => subType switch
        {
            1 => "MS-CHAP-Response",
            2 => "MS-CHAP-Error",
            7 => "MS-CHAP-CPW-1",
            8 => "MS-CHAP-CPW-2",
            9 => "MS-CHAP-LM-Enc-PW",
            10 => "MS-CHAP-NT-Enc-PW",
            11 => "MS-CHAP-Challenge",
            12 => "MS-CHAP-MPPE-Keys",
            25 => "MS-CHAP2-Response",
            26 => "MS-CHAP2-Success",
            27 => "MS-CHAP2-CPW",
            _ => $"SubType-{subType}"
        };
    }
}
