// Options.cs - コマンドライン / GUI 共通の設定オプション (検証・既定値適用)

using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace RadiusTest
{
    // ------------------------------------------------------------------
    // 設定オプション
    // ------------------------------------------------------------------
    public class Options
    {
        public bool RadSec;
        public bool StatusServer;
        public bool TlsOnly;
        public bool MsgAuth = true;      // Access-Request における Message-Authenticator 属性 (BlastRADIUS 脆弱性対策)
        public bool Brief;
        public bool NoHex;
        public bool Insecure;
        public bool NoNameCheck;
        public bool Ipv6;                // IPv6 優先 / 強制 (-6 / -ipv6)
        public bool Ipv4;                // IPv4 優先 / 強制 (-4 / -ipv4)

        public string AuthMethod = "pap"; // pap | chap | mschapv2

        public string? Server;
        public string? Secret;
        public string? User;
        public string? Pass;

        public int Port;
        public int Timeout = 3;
        public int Retries = 2;
        public int? NasPort = 0;
        public int ServiceType = 1;

        public string? NasIp;
        public string? NasIpv6;
        public string? NasId;
        public int? NasPortType;         // Type 61: NAS-Port-Type (RFC 2865)
        public string? NasPortId;        // Type 87: NAS-Port-Id (RFC 2869 / 3580)
        public string? Calling;
        public string? Called;           // Type 30: Called-Station-Id (RFC 2865 / 3580)

        public string? Sni;
        public string Tls = "auto";      // auto | 1.3 | 1.2 | 1.1 | 1.0
        public string? CertFile;         // .pfx または .crt / .pem
        public string? KeyFile;          // .key / .pem 秘密鍵 (PEM 形式証明書用)
        public string? CertPass;         // PFX パスワードまたは暗号化 PEM パスフレーズ
        public string? Thumb;
        public string? CertSubject;
        public string StoreName = "My";
        public string StoreLoc = "CurrentUser";
        public List<string> CaFiles = new List<string>();

        // EAP 認証拡張
        public string? Eap;               // null | tls | ttls | peap | gtc | fast | teap
        public string? OuterId;
        public string TtlsInner = "pap";  // pap | mschapv2 | chap
        public int EapMtu = 1000;

        public List<string> Attrs = new List<string>();
        public List<string> AttrsHex = new List<string>();

        public bool UsesTls => RadSec || (Eap != null && Eap != "gtc");

        /// <summary>
        /// オプションの整合性を検証し、既定値を適用します。
        /// </summary>
        public void Finish()
        {
            if (string.IsNullOrWhiteSpace(Server)) throw new ArgumentException("サーバーのアドレスまたはホスト名は必須です。");

            // 角括弧表記 [2001:...%scope] や [::1] のトリム
            Server = Server.Trim();
            if (Server.StartsWith("[") && Server.EndsWith("]"))
            {
                Server = Server.Substring(1, Server.Length - 2);
                Ipv6 = true;
            }
            else if (Server.Contains(':') && !IPAddress.TryParse(Server, out _))
            {
                // [fe80::1]:1812 のように角括弧付きでポートが含まれている場合
                if (Server.StartsWith("[") && Server.Contains("]:"))
                {
                    int closing = Server.IndexOf("]:");
                    string srv = Server.Substring(1, closing - 1);
                    string portStr = Server.Substring(closing + 2);
                    if (int.TryParse(portStr, out int p)) Port = p;
                    Server = srv;
                    Ipv6 = true;
                }
            }
            else if (Server.Contains(':'))
            {
                // IPv6 リテラルアドレス（コロンを含む）
                Ipv6 = true;
            }

            if (!string.IsNullOrEmpty(NasIp))
            {
                NasIp = NasIp.Trim();
                if (NasIp.StartsWith("[") && NasIp.EndsWith("]"))
                    NasIp = NasIp.Substring(1, NasIp.Length - 2);
            }

            if (RadSec && Secret == null) Secret = "radsec";
            if (!RadSec && Secret == null) throw new ArgumentException("RADIUS/UDP モードでは共有シークレット（Secret）が必須です。");
            if (Port == 0) Port = RadSec ? 2083 : 1812;
            if (Port < 1 || Port > 65535) throw new ArgumentException("無効なポート番号です（1〜65535 の範囲で指定してください）。");
            if (Timeout < 1) Timeout = 1;
            if (Retries < 0) Retries = 0;

            AuthMethod = AuthMethod.ToLowerInvariant();
            if (AuthMethod != "pap" && AuthMethod != "chap" && AuthMethod != "mschapv2" && AuthMethod != "mschap")
                throw new ArgumentException("認証方式は pap, chap, mschapv2 のいずれかを指定してください。");
            if (AuthMethod == "mschap") AuthMethod = "mschapv2";

            if (Eap != null)
            {
                Eap = Eap.ToLowerInvariant();
                if (Eap == "peapv0") Eap = "peap";
                if (Eap != "tls" && Eap != "ttls" && Eap != "peap" && Eap != "peapv1" && Eap != "gtc" && Eap != "fast" && Eap != "teap")
                    throw new ArgumentException("-eap は tls, ttls, peap(v0), peapv1, gtc, fast, teap のいずれかを指定してください。");
                if (StatusServer) throw new ArgumentException("-status と -eap は同時に指定できません。");
                if (string.IsNullOrEmpty(User)) throw new ArgumentException("EAP 認証にはユーザー名（-user）が必要です。");
                if (Eap == "tls" && CertFile == null && Thumb == null && CertSubject == null)
                    throw new ArgumentException("EAP-TLS にはクライアント証明書（-cert / -thumb / -certsubject）が必要です。");
                if ((Eap == "ttls" || Eap == "peap" || Eap == "peapv1" || Eap == "gtc" || Eap == "fast" || Eap == "teap") && string.IsNullOrEmpty(Pass))
                    throw new ArgumentException($"{(Eap.StartsWith("peap") ? Eap.ToUpperInvariant() : "EAP-" + Eap.ToUpperInvariant())} にはパスワード（-pass）が必要です。");
                if (string.IsNullOrWhiteSpace(OuterId))
                {
                    OuterId = User;
                }
                if (EapMtu < 64 || EapMtu > 4096)
                    throw new ArgumentException("EAP MTU は 64 〜 4096 バイトの範囲で指定してください。");

                if (Eap == "ttls")
                {
                    TtlsInner = TtlsInner.ToLowerInvariant();
                    if (TtlsInner == "mschap") TtlsInner = "mschapv2";
                    if (TtlsInner != "pap" && TtlsInner != "mschapv2" && TtlsInner != "chap")
                        throw new ArgumentException("EAP-TTLS 内部認証方式 (-ttls-inner) は pap, mschapv2, chap のいずれかを指定してください。");
                }
            }
            else if (User == null && !StatusServer)
            {
                if (RadSec) TlsOnly = true;
                else throw new ArgumentException("ユーザー名（-user）が必須です（または -status を指定してください）。");
            }

            if (!StatusServer && (AuthMethod == "chap" || AuthMethod == "mschapv2") && string.IsNullOrEmpty(Pass))
            {
                throw new ArgumentException($"{AuthMethod.ToUpperInvariant()} 認証にはパスワード（-pass）が必須です。");
            }

            if (TlsOnly && !RadSec) throw new ArgumentException("-tlsonly には -radsec の指定が必要です。");
            if (!string.IsNullOrEmpty(NasIp) && !NasIp.Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                var parts = NasIp.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var p in parts)
                {
                    string trimmed = p.Trim();
                    if (trimmed.Equals("none", StringComparison.OrdinalIgnoreCase) || trimmed.Equals("auto", StringComparison.OrdinalIgnoreCase)) continue;
                    if (trimmed.StartsWith("[") && trimmed.EndsWith("]")) trimmed = trimmed.Substring(1, trimmed.Length - 2);
                    if (!IPAddress.TryParse(trimmed, out _)) throw new ArgumentException($"無効な NAS-IP-Address です: '{p}'");
                }
            }

            if (!string.IsNullOrEmpty(NasIpv6) && !NasIpv6.Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                string cleanV6 = NasIpv6.Trim();
                if (cleanV6.StartsWith("[") && cleanV6.EndsWith("]")) cleanV6 = cleanV6.Substring(1, cleanV6.Length - 2);
                if (!IPAddress.TryParse(cleanV6, out var v6) || v6.AddressFamily != System.Net.Sockets.AddressFamily.InterNetworkV6)
                    throw new ArgumentException($"無効な NAS-IPv6-Address です: '{NasIpv6}' (IPv6 アドレス形式で指定してください)");
            }

            Tls = Tls.ToLowerInvariant();
            if (Tls != "1.3" && Tls != "1.2" && Tls != "1.1" && Tls != "1.0" && Tls != "auto")
                throw new ArgumentException("TLS バージョンは 1.3, 1.2, 1.1, 1.0, auto のいずれかを指定してください。");

            if (Pass != null && Encoding.UTF8.GetByteCount(Pass) > 128)
                throw new ArgumentException("パスワードが 128 バイトを超えています。");

            if (!UsesTls && (CertFile != null || KeyFile != null || Thumb != null || CertSubject != null || CaFiles.Count > 0 || Insecure))
                throw new ArgumentException("TLS 関連オプションは -radsec または -eap 指定時のみ有効です。");

            if (!UsesTls) Sni = null;

            // 属性パラメータ構文の事前チェック
            RadiusPacket dummy = new RadiusPacket();
            foreach (string s in Attrs) Runner.AddExtra(dummy, s, false);
            foreach (string s in AttrsHex) Runner.AddExtra(dummy, s, true);
        }
    }
}
