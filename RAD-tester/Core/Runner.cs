// Runner.cs - テスト実行エンジン (名前解決 → 接続 → 要求/応答 → 検証)

using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace RadiusTest
{
    // ------------------------------------------------------------------
    // テスト実行エンジン (Runner)
    // ------------------------------------------------------------------
    public static class Runner
    {
        public static Options O = new Options();

        // 相互認証検証用の MS-CHAPv2 セッション状態
        private static byte[]? _lastMsAuthChallenge;
        private static byte[]? _lastMsPeerChallenge;
        private static byte[]? _lastMsNtResponse;

        public static void AddExtra(RadiusPacket p, string spec, bool isHex)
        {
            int eq = spec.IndexOf('=');
            if (eq <= 0) throw new ArgumentException($"属性の指定形式が正しくありません: '{spec}' (name=value 形式で指定してください)");
            string key = spec.Substring(0, eq).Trim();
            string val = spec.Substring(eq + 1);

            byte? type = Dict.Lookup(key);
            if (!type.HasValue) throw new ArgumentException($"未定義の属性名または番号です: '{key}'");

            byte[] bytes;
            if (isHex)
            {
                string clean = val.Replace(" ", "").Replace("0x", "");
                if (clean.Length % 2 != 0) throw new ArgumentException($"16進数値 '{val}' の桁数は偶数である必要があります。");
                try
                {
                    bytes = Convert.FromHexString(clean);
                }
                catch (FormatException ex)
                {
                    throw new ArgumentException($"無効な 16 進数文字列です: '{val}' ({ex.Message})");
                }
            }
            else
            {
                if (IPAddress.TryParse(val, out var ip)) bytes = ip.GetAddressBytes();
                else if (uint.TryParse(val, out uint n))
                    bytes = new byte[] { (byte)(n >> 24), (byte)(n >> 16), (byte)(n >> 8), (byte)n };
                else
                    bytes = Encoding.UTF8.GetBytes(val);
            }
            p.Attrs.Add(new RadiusAttr(type.Value, bytes));
        }

        /// <summary>
        /// 有効なネットワークインターフェースから指定されたファミリの非ループバック IP を検索します。
        /// </summary>
        public static IPAddress? FindLocalIp(AddressFamily family)
        {
            try
            {
                foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback) continue;
                    foreach (var ip in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (ip.Address.AddressFamily == family && !IPAddress.IsLoopback(ip.Address))
                        {
                            if (family == AddressFamily.InterNetworkV6 && ip.Address.IsIPv6LinkLocal) continue;
                            return ip.Address;
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// NAS IP 属性 (NAS-IP-Address: Type 4 / NAS-IPv6-Address: Type 95) を追加します。
        /// - IPv4 接続時: NAS-IP-Address を自動設定 (未指定時)。NAS-IPv6-Address は明示指定がなければ送信しない。
        /// - IPv6 接続時: NAS-IPv6-Address を自動設定 (未指定時)。NAS-IP-Address は明示指定がなければ送信しない。
        /// - 両方指定された場合、または接続種別と異なるプロトコルが指定された場合は両方送信されます (RFC 3162)。
        /// - "none" が指定された場合は該当属性の送信を抑止します。
        /// </summary>
        public static void AddNasIpAttr(RadiusPacket req, string? nasIpStr, string? nasIpv6Str, IPAddress? autoLocalIp = null)
        {
            // nasIpStr にカンマ等で IPv6 が含まれている場合の互換処理
            if (!string.IsNullOrWhiteSpace(nasIpStr) && (nasIpStr.Contains(',') || nasIpStr.Contains(';')))
            {
                var parts = nasIpStr.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                string? v4 = null;
                string? v6 = null;
                foreach (var part in parts)
                {
                    string trimmed = part.Trim();
                    if (IPAddress.TryParse(trimmed, out var ip))
                    {
                        if (ip.AddressFamily == AddressFamily.InterNetwork && v4 == null) v4 = trimmed;
                        else if (ip.AddressFamily == AddressFamily.InterNetworkV6 && v6 == null) v6 = trimmed;
                    }
                }
                if (v4 != null) nasIpStr = v4;
                if (v6 != null && string.IsNullOrWhiteSpace(nasIpv6Str)) nasIpv6Str = v6;
            }
            // nasIpStr 単独に IPv6 アドレスが渡された場合の互換処理
            else if (!string.IsNullOrWhiteSpace(nasIpStr) && string.IsNullOrWhiteSpace(nasIpv6Str))
            {
                string clean = nasIpStr.Trim();
                if (clean.StartsWith("[") && clean.EndsWith("]")) clean = clean.Substring(1, clean.Length - 2);
                if (IPAddress.TryParse(clean, out var parsed) && parsed.AddressFamily == AddressFamily.InterNetworkV6)
                {
                    nasIpv6Str = clean;
                    nasIpStr = null;
                }
            }

            bool isLocalV4 = autoLocalIp?.AddressFamily == AddressFamily.InterNetwork;
            bool isLocalV6 = autoLocalIp?.AddressFamily == AddressFamily.InterNetworkV6;

            // 1. NAS-IP-Address (Type 4: IPv4)
            if (!string.IsNullOrWhiteSpace(nasIpStr))
            {
                if (!nasIpStr.Equals("none", StringComparison.OrdinalIgnoreCase))
                {
                    if (nasIpStr.Equals("auto", StringComparison.OrdinalIgnoreCase))
                    {
                        var ip = isLocalV4 ? autoLocalIp : FindLocalIp(AddressFamily.InterNetwork);
                        if (ip != null) req.Attrs.Add(new RadiusAttr(4, ip.GetAddressBytes()));
                    }
                    else if (IPAddress.TryParse(nasIpStr, out var ip) && ip.AddressFamily == AddressFamily.InterNetwork)
                    {
                        req.Attrs.Add(new RadiusAttr(4, ip.GetAddressBytes()));
                    }
                }
            }
            else if (isLocalV4)
            {
                req.Attrs.Add(new RadiusAttr(4, autoLocalIp!.GetAddressBytes()));
            }

            // 2. NAS-IPv6-Address (Type 95: IPv6)
            if (!string.IsNullOrWhiteSpace(nasIpv6Str))
            {
                if (!nasIpv6Str.Equals("none", StringComparison.OrdinalIgnoreCase))
                {
                    if (nasIpv6Str.Equals("auto", StringComparison.OrdinalIgnoreCase))
                    {
                        var ip = isLocalV6 ? autoLocalIp : FindLocalIp(AddressFamily.InterNetworkV6);
                        if (ip != null) req.Attrs.Add(new RadiusAttr(95, ip.GetAddressBytes()));
                    }
                    else
                    {
                        string clean = nasIpv6Str.Trim();
                        if (clean.StartsWith("[") && clean.EndsWith("]")) clean = clean.Substring(1, clean.Length - 2);
                        if (IPAddress.TryParse(clean, out var ip) && ip.AddressFamily == AddressFamily.InterNetworkV6)
                        {
                            req.Attrs.Add(new RadiusAttr(95, ip.GetAddressBytes()));
                        }
                    }
                }
            }
            else if (isLocalV6)
            {
                req.Attrs.Add(new RadiusAttr(95, autoLocalIp!.GetAddressBytes()));
            }
        }

        public static void AddNasIpAttr(RadiusPacket req, string? nasIpStr, IPAddress? autoLocalIp = null)
            => AddNasIpAttr(req, nasIpStr, null, autoLocalIp);

        private static RadiusPacket BuildRequest(byte id, byte[] auth, byte[] secret, byte[]? state = null, string? promptResp = null, IPAddress? localIp = null)
        {
            var p = new RadiusPacket
            {
                Code = (byte)(O.StatusServer ? 12 : 1),
                Identifier = id,
                Authenticator = auth
            };

            if (O.StatusServer)
            {
                p.Attrs.Add(new RadiusAttr(80, new byte[16]));
                AddNasIpAttr(p, O.NasIp, O.NasIpv6, localIp);
                return p;
            }

            p.Attrs.Add(new RadiusAttr(1, Encoding.UTF8.GetBytes(O.User!)));

            string effectivePass = promptResp ?? O.Pass ?? "";

            // 認証方式別の属性付与
            if (O.AuthMethod == "chap")
            {
                // RFC 2865 / RFC 1994: CHAP-Challenge (Type 60) + CHAP-Password (Type 3)
                byte[] chapChallenge = new byte[16];
                RandomNumberGenerator.Fill(chapChallenge);
                byte[] chapPassword = Crypto.GenerateChapPassword(id, effectivePass, chapChallenge);

                p.Attrs.Add(new RadiusAttr(60, chapChallenge));
                p.Attrs.Add(new RadiusAttr(3, chapPassword));
            }
            else if (O.AuthMethod == "mschapv2")
            {
                // RFC 2548 / RFC 2759: MS-CHAP-Challenge (311/11) + MS-CHAP2-Response (311/25)
                byte[] authChallenge = new byte[16];
                RandomNumberGenerator.Fill(authChallenge);
                byte[] peerChallenge = new byte[16];
                RandomNumberGenerator.Fill(peerChallenge);

                byte[] ntResponse = Crypto.GenerateMsChap2Response(O.User!, effectivePass, authChallenge, peerChallenge);

                _lastMsAuthChallenge = authChallenge;
                _lastMsPeerChallenge = peerChallenge;
                _lastMsNtResponse = ntResponse;

                byte[] msChap2Resp = new byte[50];
                msChap2Resp[0] = id;       // Ident
                msChap2Resp[1] = 0;        // Flags
                Buffer.BlockCopy(peerChallenge, 0, msChap2Resp, 2, 16);
                Buffer.BlockCopy(ntResponse, 0, msChap2Resp, 26, 24);

                p.Attrs.Add(RadiusAttr.CreateVsa(311, 11, authChallenge));
                p.Attrs.Add(RadiusAttr.CreateVsa(311, 25, msChap2Resp));
            }
            else
            {
                // 既定: PAP (User-Password)
                if (promptResp != null || O.Pass != null)
                {
                    byte[] enc = Crypto.EncryptPassword(effectivePass, auth, secret);
                    p.Attrs.Add(new RadiusAttr(2, enc));
                }
            }

            if (state != null) p.Attrs.Add(new RadiusAttr(24, state));

            AddNasIpAttr(p, O.NasIp, O.NasIpv6, localIp);
            if (O.NasId != null) p.Attrs.Add(new RadiusAttr(32, Encoding.UTF8.GetBytes(O.NasId)));
            if (O.Calling != null) p.Attrs.Add(new RadiusAttr(31, Encoding.UTF8.GetBytes(O.Calling)));
            if (O.Called != null) p.Attrs.Add(new RadiusAttr(30, Encoding.UTF8.GetBytes(O.Called)));
            if (O.NasPortType.HasValue)
            {
                uint pt = (uint)O.NasPortType.Value;
                p.Attrs.Add(new RadiusAttr(61, new byte[] { (byte)(pt >> 24), (byte)(pt >> 16), (byte)(pt >> 8), (byte)pt }));
            }
            if (O.NasPortId != null) p.Attrs.Add(new RadiusAttr(87, Encoding.UTF8.GetBytes(O.NasPortId)));
            if (O.ServiceType >= 0)
            {
                uint st = (uint)O.ServiceType;
                p.Attrs.Add(new RadiusAttr(6, new byte[] { 0, 0, 0, (byte)st }));
            }
            if (O.NasPort.HasValue)
            {
                uint np = (uint)O.NasPort.Value;
                p.Attrs.Add(new RadiusAttr(5, new byte[] { (byte)(np >> 24), (byte)(np >> 16), (byte)(np >> 8), (byte)np }));
            }

            if (O.MsgAuth) p.Attrs.Add(new RadiusAttr(80, new byte[16]));

            foreach (string a in O.Attrs) AddExtra(p, a, false);
            foreach (string a in O.AttrsHex) AddExtra(p, a, true);

            return p;
        }

        private static int RunAuth(ITransport t, byte[] secret)
        {
            byte id = (byte)RandomNumberGenerator.GetInt32(0, 256);
            byte[] reqAuth = new byte[16];
            RandomNumberGenerator.Fill(reqAuth);

            byte[]? state = null;
            string? promptResponse = null;

            for (int round = 1; round <= 8; round++)
            {
                var req = BuildRequest(id, reqAuth, secret, state, promptResponse, t.LocalEndPoint?.Address);
                byte[] rawReq = req.Encode(secret);

                string methodTag = O.StatusServer ? "" : $" [{O.AuthMethod.ToUpperInvariant()}]";
                string reqDesc = O.StatusServer ? "Status-Server" : (round == 1 ? $"Access-Request{methodTag}" : $"Access-Request{methodTag} (ラウンド {round})");
                string sendFlow = O.StatusServer ? "Supplicant --> RADIUS" : "Authenticator --> RADIUS";
                string recvFlow = O.StatusServer ? "Supplicant <-- RADIUS" : "Authenticator <-- RADIUS";

                Progress.Log("BUILD", PState.Ok, $"{reqDesc} id={id} 属性数={req.Attrs.Count} パケット長={rawReq.Length} バイト", sendFlow);

                if (!O.Brief) Printer.PrintPacket(req, $"[{sendFlow}] 送信 {reqDesc}", !O.NoHex);

                int maxAttempts = O.RadSec ? 1 : 1 + O.Retries;
                byte[]? rawResp = null;
                var sw = Stopwatch.StartNew();

                for (int attempt = 1; attempt <= maxAttempts; attempt++)
                {
                    Progress.Log("SEND", PState.Ok, $"{rawReq.Length} バイト送信 (試行 {attempt}/{maxAttempts})", sendFlow);
                    t.Send(rawReq);

                    Progress.Log("RECV", PState.Info, $"応答待機 (タイムアウト {O.Timeout}秒)", recvFlow);
                    sw.Restart();
                    rawResp = t.Receive(O.Timeout * 1000);
                    sw.Stop();
                    if (rawResp != null) break;

                    if (attempt < maxAttempts)
                        Progress.Log("RECV", PState.Warn, $"タイムアウト ({O.Timeout}秒)、再送", recvFlow);
                }

                if (rawResp == null)
                {
                    Progress.Log("RECV", PState.Fail, $"サーバーから応答がありませんでした ({maxAttempts} 回試行)", recvFlow);
                    return 3;
                }

                RadiusPacket resp;
                try { resp = RadiusPacket.Parse(rawResp, 0, rawResp.Length); }
                catch (Exception ex)
                {
                    Progress.Log("RECV", PState.Fail, $"不正な応答パケットを受信しました: {ex.Message}", recvFlow);
                    return 5;
                }

                long rtt = sw.ElapsedMilliseconds;
                Progress.Log("RECV", PState.Ok, $"{Dict.CodeName(resp.Code)} (コード {resp.Code}) id={resp.Identifier}, {rawResp.Length} バイト, RTT {rtt} ms", recvFlow);

                if (!O.Brief) Printer.PrintPacket(resp, $"[{recvFlow}] 受信 {Dict.CodeName(resp.Code)}", !O.NoHex);

                // レスポンス検証 (接続試験用のため、異常値は警告報告してできる限り継続)
                bool verifyOk = true;
                if (resp.Malformed)
                {
                    Progress.Log("VERIFY", PState.Warn, "パケットヘッダ長または属性フォーマットに異常があります (Malformed Packet、試験を継続)");
                    verifyOk = false;
                }
                if (resp.Identifier != id)
                {
                    Progress.Log("VERIFY", PState.Warn, $"識別子 (Identifier) 不一致: 送信={id}, 受信={resp.Identifier} (試験を継続)");
                    verifyOk = false;
                }

                bool authOk = Crypto.VerifyResponseAuth(rawResp, reqAuth, secret);
                bool msgAuthOk = Crypto.VerifyMessageAuth(rawResp, reqAuth, secret);

                if (!authOk)
                {
                    if (O.RadSec)
                        Progress.Log("VERIFY", PState.Warn, "Response Authenticator 不一致 (RFC 6614 RadSec: TLS 暗号化により完全性は保護されています)");
                    else
                    {
                        Progress.Log("VERIFY", PState.Warn, "Response Authenticator 検証不一致 (共有シークレット不一致または改ざんの可能性、試験を継続)");
                        verifyOk = false;
                    }
                }
                if (!msgAuthOk)
                {
                    Progress.Log("VERIFY", PState.Warn, "Message-Authenticator 検証不一致 (試験を継続)");
                    verifyOk = false;
                }

                if (verifyOk)
                {
                    Progress.Log("VERIFY", PState.Ok, "パケット長/ID 正常, Response Authenticator 正常, Message-Authenticator 正常");
                }

                // 結果判定 (RFC 2865: 複数の Reply-Message がある場合は改行で結合)
                var replyMsgs = new System.Collections.Generic.List<string>();
                foreach (var a in resp.Attrs)
                {
                    if (a.Type == 18 && a.Value.Length > 0)
                        replyMsgs.Add(Encoding.UTF8.GetString(a.Value));
                }
                string replyNote = replyMsgs.Count > 0 ? $" (Reply-Message: \"{string.Join(" / ", replyMsgs)}\")" : "";

                // MS-CHAPv2 の相互認証検証
                if (O.AuthMethod == "mschapv2" && _lastMsAuthChallenge != null && _lastMsPeerChallenge != null && _lastMsNtResponse != null)
                {
                    var msSuccess = resp.GetMsVsa(26); // MS-CHAP2-Success
                    if (msSuccess.HasValue)
                    {
                        string succStr = Encoding.ASCII.GetString(msSuccess.Value.SubData);
                        string expectedAuth = Crypto.GenerateAuthenticatorResponse(O.Pass!, _lastMsNtResponse, _lastMsPeerChallenge, _lastMsAuthChallenge, O.User!);
                        if (succStr.IndexOf(expectedAuth, StringComparison.OrdinalIgnoreCase) >= 0)
                            Progress.Info("VERIFY", $"MS-CHAPv2 相互認証成功（サーバー署名一致: S={expectedAuth}）");
                        else
                            Progress.Log("VERIFY", PState.Warn, $"MS-CHAPv2 サーバー認証不一致: 受信=\"{succStr}\", 期待値=\"S={expectedAuth}\"");
                    }

                    var msError = resp.GetMsVsa(2); // MS-CHAP-Error
                    if (msError.HasValue)
                    {
                        string errStr = Encoding.ASCII.GetString(msError.Value.SubData);
                        string errNote = errStr.IndexOf("E=691", StringComparison.OrdinalIgnoreCase) >= 0
                            ? " [E=691: 認証失敗（パスワード誤りまたはユーザーが存在しません）]"
                            : "";
                        Progress.Info("RESULT", $"MS-CHAP-Error: \"{errStr}\"{errNote}");
                    }
                }

                switch (resp.Code)
                {
                    case 2: // Access-Accept
                        Progress.Log("RESULT", PState.Ok, O.StatusServer ? "サーバー生存確認成功: Access-Accept" : $"認証成功: Access-Accept{replyNote}", recvFlow);
                        return 0;
                    case 3: // Access-Reject
                        Progress.Log("RESULT", PState.Fail, $"認証拒否: Access-Reject{replyNote}", recvFlow);
                        return 1;
                    case 11: // Access-Challenge
                        state = resp.Get(24);
                        Progress.Log("RESULT", PState.Warn, $"Access-Challenge 受信 (ラウンド {round}){replyNote}", recvFlow);
                        if (Out.PromptFunc != null)
                        {
                            promptResponse = Out.PromptFunc("チャレンジ応答（ワンタイムパスワード等）を入力してください: ");
                            if (!string.IsNullOrEmpty(promptResponse))
                            {
                                id++;
                                continue;
                            }
                        }
                        else if (Out.Prompt != null && !Console.IsInputRedirected)
                        {
                            Out.Prompt("チャレンジ応答を入力してください: ");
                            promptResponse = Console.ReadLine();
                            if (!string.IsNullOrEmpty(promptResponse))
                            {
                                id++;
                                continue;
                            }
                        }
                        Progress.Log("RESULT", PState.Ok, $"Access-Challenge を受領しました (対話プロンプトなしのため完了){replyNote}", recvFlow);
                        return 0;
                    default:
                        Progress.Log("RESULT", PState.Warn, $"想定外の応答コードを受信しました: {resp.Code} ({Dict.CodeName(resp.Code)}){replyNote}");
                        return 1;
                }
            }
            Progress.Log("RESULT", PState.Fail, "Access-Challenge のラウンド数が上限（8回）に達しました。");
            return 4;
        }

        private static int RunInner()
        {
            string modeDesc = O.RadSec ? "RadSec(TLS)" : "RADIUS/UDP";
            if (!O.StatusServer) modeDesc += $" ({O.AuthMethod.ToUpperInvariant()})";

            Progress.Log("INIT", PState.Ok, $"モード={modeDesc}, サーバー={O.Server}:{O.Port}, ユーザー={O.User ?? "(なし)"}, タイムアウト={O.Timeout}秒{(O.StatusServer ? ", Status-Server" : (O.TlsOnly ? ", TLS専用テスト" : ""))}");

            TlsContext? tls = null;
            ITransport t;
            if (O.UsesTls)
            {
                tls = new TlsContext(O, "TLS");
                try { tls.Load(); }
                catch (Exception ex)
                {
                    Progress.Log("INIT", PState.Fail, $"証明書読み込みエラー: {ex.Message}");
                    return 2;
                }
            }

            if (O.RadSec) t = new RadSecTransport(O, tls!);
            else
            {
                Progress.Skip("TLS", "未使用 (プレーン RADIUS/UDP)");
                t = new UdpTransport(O);
            }

            // 名前解決
            IPAddress? ip = null;
            try
            {
                if (IPAddress.TryParse(O.Server, out var parsed))
                {
                    ip = parsed;
                }
                else
                {
                    var addrs = Dns.GetHostAddresses(O.Server!);
                    if (O.Ipv6)
                    {
                        foreach (var a in addrs) if (a.AddressFamily == AddressFamily.InterNetworkV6) { ip = a; break; }
                        if (ip == null) throw new InvalidOperationException($"ホスト '{O.Server}' の IPv6 (AAAA) アドレスが見つかりませんでした。");
                    }
                    else if (O.Ipv4)
                    {
                        foreach (var a in addrs) if (a.AddressFamily == AddressFamily.InterNetwork) { ip = a; break; }
                        if (ip == null) throw new InvalidOperationException($"ホスト '{O.Server}' の IPv4 (A) アドレスが見つかりませんでした。");
                    }
                    else
                    {
                        // デフォルト: IPv4 を優先、存在しなければ IPv6
                        foreach (var a in addrs) if (a.AddressFamily == AddressFamily.InterNetwork) { ip = a; break; }
                        if (ip == null)
                        {
                            foreach (var a in addrs) if (a.AddressFamily == AddressFamily.InterNetworkV6) { ip = a; break; }
                        }
                    }
                    if (ip == null && addrs.Length > 0) ip = addrs[0];
                }
                if (ip == null) throw new InvalidOperationException("ホストのアドレスを取得できませんでした。");
                string ipFamily = ip.AddressFamily == AddressFamily.InterNetworkV6 ? "IPv6" : "IPv4";
                Progress.Log("RESOLVE", PState.Ok, $"{O.Server} -> [{ipFamily}] {ip}", "Supplicant --> DNS");
            }
            catch (Exception ex)
            {
                Progress.Log("RESOLVE", PState.Fail, $"'{O.Server}' の名前解決に失敗しました: {ex.Message}", "Supplicant --> DNS");
                return 3;
            }

            try
            {
                if (!t.Open(new IPEndPoint(ip, O.Port))) return 3;
                if (O.TlsOnly)
                {
                    Progress.Skip("BUILD", "スキップ (TLS専用テスト)");
                    Progress.Skip("SEND", "スキップ (TLS専用テスト)");
                    Progress.Skip("RECV", "スキップ (TLS専用テスト)");
                    Progress.Skip("VERIFY", "スキップ (TLS専用テスト)");
                    Progress.Log("RESULT", PState.Ok, "TLS 接続確立完了（RADIUS リクエストは未送信）", "Supplicant <--> RADIUS");
                    return 0;
                }

                byte[] secret = Encoding.UTF8.GetBytes(O.Secret!);
                if (O.Eap != null)
                {
                    var eapSession = new EapSession(O, t, secret, tls!);
                    return eapSession.Run();
                }
                return RunAuth(t, secret);
            }
            finally
            {
                t.Close();
            }
        }

        public static int Run(Options o)
        {
            O = o;
            Progress.Reset();
            int rc;
            try
            {
                rc = RunInner();
            }
            catch (Exception ex)
            {
                Progress.Log("RESULT", PState.Fail, $"予期せぬエラーが発生しました: {ex.GetType().Name}: {ex.Message}");
                rc = 3;
            }
            Progress.PrintSummary();
            return rc;
        }
    }
}
