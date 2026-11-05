// EapSession.cs - EAP セッションマネージャー (共通処理: RADIUS 送受信 / TLS 断片化) (.NET 10)
//
// 対応方式:
//   - EAP-TLS (RFC 5216): クライアント証明書による相互 TLS 認証
//   - EAP-TTLS (RFC 5281): TLS トンネル + 内部 Diameter AVP (Inner PAP) 認証
//   - PEAP (MS-PEAP / PEAPv0 / RFC 2759): TLS トンネル + 内部 MS-CHAPv2 認証
//   - EAP-GTC (RFC 3748): チャレンジプロンプトに対するトークン / パスワード認証
//   - EAP-FAST (RFC 4851): TLS トンネル + 内部 TLV / MS-CHAPv2 認証
//   - EAP-TEAP (RFC 7170): 次世代 TLS トンネル + 内部 TLV / Basic-Password-Auth / MS-CHAPv2 認証

using System;
using System.Diagnostics;
using System.IO;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RadiusTest
{
    // ------------------------------------------------------------------
    // EAP セッションマネージャー
    // ------------------------------------------------------------------
    public partial class EapSession
    {
        private readonly Options _o;
        private readonly ITransport _transport;
        private readonly byte[] _secret;
        private readonly TlsContext? _tlsCtx;
        private readonly byte _eapMethodType; // 6=GTC, 13=EAP-TLS, 21=EAP-TTLS, 25=PEAP, 43=FAST, 55=TEAP
        private readonly string _methodName;

        private byte _radiusId;
        private byte[]? _state;
        private readonly byte[] _reqAuth = new byte[16];

        public EapSession(Options o, ITransport transport, byte[] secret, TlsContext? tlsCtx)
        {
            _o = o;
            _transport = transport;
            _secret = secret;
            _tlsCtx = tlsCtx;

            _eapMethodType = _o.Eap switch
            {
                "gtc" => 6,
                "fast" => 43,
                "teap" => 55,
                "peap" or "peapv0" or "peapv1" => 25,
                "ttls" => 21,
                _ => 13
            };
            _methodName = _eapMethodType switch
            {
                6 => "EAP-GTC",
                43 => "EAP-FAST",
                55 => "EAP-TEAP",
                25 => _o.Eap == "peapv1" ? "PEAPv1" : "PEAPv0",
                21 => "EAP-TTLS",
                _ => "EAP-TLS"
            };

            _radiusId = (byte)RandomNumberGenerator.GetInt32(0, 256);
            RandomNumberGenerator.Fill(_reqAuth);
        }

        private static void AddEapMessage(RadiusPacket req, byte[] eapBytes)
        {
            int offset = 0;
            while (offset < eapBytes.Length)
            {
                int chunkLen = Math.Min(253, eapBytes.Length - offset);
                byte[] chunk = new byte[chunkLen];
                Buffer.BlockCopy(eapBytes, offset, chunk, 0, chunkLen);
                req.Attrs.Add(new RadiusAttr(79, chunk));
                offset += chunkLen;
            }
        }

        private RadiusPacket BuildRadiusEapRequest(byte[] eapBytes)
        {
            var req = new RadiusPacket
            {
                Code = 1, // Access-Request
                Identifier = _radiusId,
                Authenticator = _reqAuth
            };

            // 外側 (匿名) ユーザー名
            string outerUser = !string.IsNullOrWhiteSpace(_o.OuterId) ? _o.OuterId : (_o.User ?? "");
            req.Attrs.Add(new RadiusAttr(1, Encoding.UTF8.GetBytes(outerUser)));

            // EAP-Message 属性 (Type 79)
            AddEapMessage(req, eapBytes);

            // State 属性 (Challenge で返却された場合)
            if (_state != null) req.Attrs.Add(new RadiusAttr(24, _state));

            // 標準 RADIUS 属性
            Runner.AddNasIpAttr(req, _o.NasIp, _o.NasIpv6, _transport.LocalEndPoint?.Address);
            if (_o.NasId != null) req.Attrs.Add(new RadiusAttr(32, Encoding.UTF8.GetBytes(_o.NasId)));
            if (_o.Calling != null) req.Attrs.Add(new RadiusAttr(31, Encoding.UTF8.GetBytes(_o.Calling)));
            if (_o.Called != null) req.Attrs.Add(new RadiusAttr(30, Encoding.UTF8.GetBytes(_o.Called)));
            if (_o.NasPortType.HasValue)
            {
                uint pt = (uint)_o.NasPortType.Value;
                req.Attrs.Add(new RadiusAttr(61, new byte[] { (byte)(pt >> 24), (byte)(pt >> 16), (byte)(pt >> 8), (byte)pt }));
            }
            if (_o.NasPortId != null) req.Attrs.Add(new RadiusAttr(87, Encoding.UTF8.GetBytes(_o.NasPortId)));
            req.Attrs.Add(new RadiusAttr(6, new byte[] { 0, 0, 0, (byte)_o.ServiceType }));
            if (_o.NasPort.HasValue)
            {
                uint np = (uint)_o.NasPort.Value;
                req.Attrs.Add(new RadiusAttr(5, new byte[] { (byte)(np >> 24), (byte)(np >> 16), (byte)(np >> 8), (byte)np }));
            }

            // Message-Authenticator (Type 80) は EAP 認証で必須 (RFC 3579)
            req.Attrs.Add(new RadiusAttr(80, new byte[16]));

            // 追加属性
            foreach (string a in _o.Attrs) Runner.AddExtra(req, a, false);
            foreach (string a in _o.AttrsHex) Runner.AddExtra(req, a, true);

            return req;
        }

        private RadiusPacket? ExchangeRadius(RadiusPacket req)
        {
            byte[] rawReq = req.Encode(_secret);
            if (!_o.Brief) Printer.PrintPacket(req, $"[Supplicant --> RADIUS] 送信 Access-Request ({_methodName})", !_o.NoHex);

            int maxAttempts = _o.RadSec ? 1 : 1 + _o.Retries;
            byte[]? rawResp = null;
            var sw = Stopwatch.StartNew();

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                Progress.Log("SEND", PState.Ok, $"{rawReq.Length} バイト送信 (試行 {attempt}/{maxAttempts})", "Supplicant --> RADIUS");
                _transport.Send(rawReq);
                sw.Restart();
                rawResp = _transport.Receive(_o.Timeout * 1000);
                sw.Stop();
                if (rawResp != null) break;
            }

            if (rawResp == null)
            {
                Progress.Log("RECV", PState.Fail, $"RADIUS サーバーから応答がありませんでした ({maxAttempts} 回試行)", "Supplicant <-- RADIUS");
                return null;
            }

            RadiusPacket resp;
            try { resp = RadiusPacket.Parse(rawResp, 0, rawResp.Length); }
            catch (Exception ex)
            {
                Progress.Log("RECV", PState.Fail, $"不正な RADIUS パケット形式です: {ex.Message}", "Supplicant <-- RADIUS");
                return null;
            }

            long rtt = sw.ElapsedMilliseconds;
            string recvFlow = (resp.Code == 2 || resp.Code == 3) ? "Authenticator <-- RADIUS" : "Supplicant <-- RADIUS";
            Progress.Log("RECV", PState.Ok, $"{Dict.CodeName(resp.Code)} (コード {resp.Code}) id={resp.Identifier}, {rawResp.Length} バイト, RTT {rtt} ms", recvFlow);

            if (!_o.Brief) Printer.PrintPacket(resp, $"[{recvFlow}] 受信 {Dict.CodeName(resp.Code)}", !_o.NoHex);

            // ID およびオーセンティケータの検証
            bool verifyOk = true;
            if (resp.Identifier != _radiusId)
            {
                Progress.Log("VERIFY", PState.Warn, $"識別子 (Identifier) 不一致: 送信={_radiusId}, 受信={resp.Identifier} (試験を継続)", "Supplicant");
                verifyOk = false;
            }

            if (!Crypto.VerifyResponseAuth(rawResp, _reqAuth, _secret))
            {
                Progress.Log("VERIFY", PState.Warn, "Response Authenticator 検証不一致 (試験を継続)", "Supplicant");
                verifyOk = false;
            }
            if (!Crypto.VerifyMessageAuth(rawResp, _reqAuth, _secret))
            {
                Progress.Log("VERIFY", PState.Warn, "Message-Authenticator 検証不一致 (試験を継続)", "Supplicant");
                verifyOk = false;
            }

            if (verifyOk)
            {
                Progress.Log("VERIFY", PState.Ok, "パケット長/ID 正常, Response/Message-Authenticator 正常", "Supplicant");
            }

            _state = resp.Get(24);
            _radiusId++;
            return resp;
        }

        // 送信 TLS ペイロードを自動フラグメンテーションしてサーバーへ送信
        private EapPacket? SendTlsPayload(byte eapId, byte[] tlsBytes)
        {
            int totalLen = tlsBytes.Length;
            int mtu = _o.EapMtu;
            int offset = 0;
            bool isFirst = true;

            while (true)
            {
                int maxChunk = isFirst ? (mtu - 10) : (mtu - 6);
                int chunkLen = Math.Min(maxChunk, totalLen - offset);
                bool more = (offset + chunkLen) < totalLen;

                byte version = 0x00;
                if (_eapMethodType == 43 || _eapMethodType == 55) version = 0x01; // FAST / TEAP
                else if (_o.Eap == "peapv1") version = 0x01; // PEAPv1
                byte flags = version;
                if (isFirst) flags |= 0x80; // Length included (L)
                if (more) flags |= 0x40;    // More fragments (M)

                using var ms = new MemoryStream();
                ms.WriteByte(flags);
                if (isFirst)
                {
                    ms.WriteByte((byte)(totalLen >> 24));
                    ms.WriteByte((byte)(totalLen >> 16));
                    ms.WriteByte((byte)(totalLen >> 8));
                    ms.WriteByte((byte)totalLen);
                }
                ms.Write(tlsBytes, offset, chunkLen);
                offset += chunkLen;
                isFirst = false;

                var eapResp = new EapPacket
                {
                    Code = 2, // Response
                    Identifier = eapId,
                    Type = _eapMethodType,
                    Data = ms.ToArray()
                };

                var radiusReq = BuildRadiusEapRequest(eapResp.Encode());
                var radiusResp = ExchangeRadius(radiusReq);
                if (radiusResp == null) return null;

                byte[] eapRaw = radiusResp.Concat(79);
                var inEap = EapPacket.Parse(eapRaw, 0, eapRaw.Length);
                if (inEap == null)
                {
                    Progress.Log("EAP", PState.Fail, "サーバー応答内に有効な EAP パケットが存在しません。");
                    return null;
                }

                if (!more)
                {
                    return inEap;
                }

                // 送信側フラグメンテーション継続時の ACK 受信
                eapId = inEap.Identifier;
            }
        }

        private const int MaxTlsFragments = 64;
        private const int MaxTlsPayloadSize = 65536;

        // サーバーからのフラグメント化された TLS ペイロードを受信・再構築
        private (byte[] TlsData, byte NextEapId)? ReceiveTlsPayload(EapPacket firstPacket)
        {
            var eapPkt = firstPacket;
            using var assembledStream = new MemoryStream();
            int expectedLen = -1;
            int fragCount = 0;

            while (true)
            {
                if (++fragCount > MaxTlsFragments)
                {
                    Progress.Log("EAP", PState.Fail, $"EAP TLS フラグメント数が上限 ({MaxTlsFragments} 回) を超えました (無限ループ防止)。");
                    return null;
                }

                if (eapPkt.Code != 1)
                {
                    return (assembledStream.ToArray(), eapPkt.Identifier);
                }

                byte[] data = eapPkt.Data;
                if (data.Length == 0)
                {
                    Progress.Log("EAP", PState.Fail, "EAP メッセージ内の TLS ペイロードが空です。");
                    return null;
                }

                byte flags = data[0];
                bool hasLength = (flags & 0x80) != 0;
                bool more = (flags & 0x40) != 0;

                int off = 1;
                if (hasLength)
                {
                    if (data.Length < 5)
                    {
                        Progress.Log("EAP", PState.Fail, "EAP TLS ヘッダに Length フラグが含まれていますが、データ長が不足しています。");
                        return null;
                    }
                    expectedLen = (data[1] << 24) | (data[2] << 16) | (data[3] << 8) | data[4];
                    if (expectedLen < 0 || expectedLen > MaxTlsPayloadSize)
                    {
                        Progress.Log("EAP", PState.Fail, $"EAP TLS 宣言長 ({expectedLen} バイト) が安全上限 ({MaxTlsPayloadSize} バイト) を超えています。");
                        return null;
                    }
                    off = 5;
                }

                if (off < data.Length)
                {
                    int chunkLen = data.Length - off;
                    if (assembledStream.Length + chunkLen > MaxTlsPayloadSize)
                    {
                        Progress.Log("EAP", PState.Fail, $"EAP TLS 累積ペイロードが上限 ({MaxTlsPayloadSize} バイト) を超えました。");
                        return null;
                    }
                    assembledStream.Write(data, off, chunkLen);
                }

                if (!more)
                {
                    return (assembledStream.ToArray(), eapPkt.Identifier);
                }

                // サーバーのフラグメントに対する ACK 送信 (空の EAP-Response)
                byte ackFlags = (byte)((_eapMethodType == 43 || _eapMethodType == 55) ? 0x01 : 0x00);
                var ackResp = new EapPacket
                {
                    Code = 2,
                    Identifier = eapPkt.Identifier,
                    Type = _eapMethodType,
                    Data = new byte[] { ackFlags }
                };

                var radiusReq = BuildRadiusEapRequest(ackResp.Encode());
                var radiusResp = ExchangeRadius(radiusReq);
                if (radiusResp == null) return null;

                byte[] eapRaw = radiusResp.Concat(79);
                var nextPkt = EapPacket.Parse(eapRaw, 0, eapRaw.Length);
                if (nextPkt == null)
                {
                    Progress.Log("EAP", PState.Fail, "フラグメント ACK 後の応答に EAP パケットがありません。");
                    return null;
                }
                eapPkt = nextPkt;
            }
        }

        // ------------------------------------------------------------------
        // EAP 認証実行メインフロー
        // ------------------------------------------------------------------
        public int Run()
        {
            Progress.Log("BUILD", PState.Ok, $"{_methodName} 認証を開始します: ユーザー=\"{_o.User}\"...", "Supplicant --> RADIUS");

            // ステップ 1: 初期 EAP-Response/Identity を送信
            string outerUser = !string.IsNullOrWhiteSpace(_o.OuterId) ? _o.OuterId : (_o.User ?? "");
            var idResp = new EapPacket
            {
                Code = 2, // Response
                Identifier = 1,
                Type = 1, // Identity
                Data = Encoding.UTF8.GetBytes(outerUser)
            };

            Progress.Log("BUILD", PState.Ok, $"初期 EAP-Response/Identity を送信: ユーザー=\"{outerUser}\"", "Supplicant --> RADIUS");
            var radiusReq = BuildRadiusEapRequest(idResp.Encode());
            var radiusResp = ExchangeRadius(radiusReq);
            if (radiusResp == null) return 4;

            if (radiusResp.Code == 2)
            {
                Progress.Log("RESULT", PState.Ok, "認証成功: Access-Accept", "Authenticator <-- RADIUS");
                return 0;
            }
            if (radiusResp.Code == 3)
            {
                Progress.Log("RESULT", PState.Fail, "認証拒否: Access-Reject", "Authenticator <-- RADIUS");
                return 1;
            }

            byte[] eapBytes = radiusResp.Concat(79);
            var serverEap = EapPacket.Parse(eapBytes, 0, eapBytes.Length);
            if (serverEap == null)
            {
                Progress.Log("EAP", PState.Fail, "サーバー応答内に EAP パケットが存在しません。", "Supplicant <-- RADIUS");
                return 4;
            }

            Progress.Log("EAP", PState.Ok, $"EAP-{EapPacket.CodeName(serverEap.Code)} 受信 id={serverEap.Identifier} タイプ={EapPacket.TypeName(serverEap.Type)}", "Supplicant <-- RADIUS");

            // サーバー提案の EAP Type が不一致の場合、EAP-NAK で希望方式を要求
            if (serverEap.Type != _eapMethodType)
            {
                Progress.Log("EAP", PState.Info, $"サーバー提案 EAP タイプ: {EapPacket.TypeName(serverEap.Type)} ({serverEap.Type})。EAP-NAK を送信して {_methodName} ({_eapMethodType}) を要求", "Supplicant --> RADIUS");
                var nakResp = new EapPacket
                {
                    Code = 2,
                    Identifier = serverEap.Identifier,
                    Type = 3, // NAK
                    Data = new byte[] { _eapMethodType }
                };

                var nakReq = BuildRadiusEapRequest(nakResp.Encode());
                var nakRespRadius = ExchangeRadius(nakReq);
                if (nakRespRadius == null) return 4;

                byte[] nakEapBytes = nakRespRadius.Concat(79);
                serverEap = EapPacket.Parse(nakEapBytes, 0, nakEapBytes.Length);
                if (serverEap == null)
                {
                    Progress.Log("EAP", PState.Fail, "EAP-NAK 送信後に EAP パケットを受信できませんでした。", "Supplicant <-- RADIUS");
                    return 4;
                }

                Progress.Log("EAP", PState.Ok, $"EAP-{EapPacket.CodeName(serverEap.Code)} 受信 id={serverEap.Identifier} タイプ={EapPacket.TypeName(serverEap.Type)}", "Supplicant <-- RADIUS");
                if (serverEap.Type != _eapMethodType)
                {
                    Progress.Log("EAP", PState.Fail, $"サーバーは {_methodName} をサポートしていません（{EapPacket.TypeName(serverEap.Type)} で応答）", "Supplicant <-- RADIUS");
                    return 4;
                }
            }

            // GTC は TLS トンネルを使用せず直接認証
            if (_eapMethodType == 6)
            {
                return RunGtc(serverEap);
            }

            // TLS ベース方式 (EAP-TLS, EAP-TTLS, PEAP, EAP-FAST, EAP-TEAP) の Start (S) フラグ確認
            if (serverEap.Data.Length == 0)
            {
                Progress.Log("EAP", PState.Fail, $"サーバー EAP-{_methodName} のデータが空です (Flags フィールド欠落)");
                return 4;
            }
            if ((serverEap.Data[0] & 0x20) == 0)
            {
                Progress.Log("EAP", PState.Fail, $"サーバー EAP-{_methodName} に Start フラグが含まれていません (Flags=0x{serverEap.Data[0]:x2})");
                return 4;
            }

            if (_tlsCtx == null)
            {
                Progress.Log("TLS", PState.Fail, $"{_methodName} には TLS コンテキストが必要です。");
                return 2;
            }

            Progress.Log("TLS", PState.Ok, $"{_methodName} 開始フラグ (Start) を検出しました。仮想ストリーム上で TLS ハンドシェイクを開始");

            using var vStream = new EapTlsTransportStream();
            using var ssl = new SslStream(vStream, false, _tlsCtx.ValidateServerCertificate, _tlsCtx.SelectClientCertificate);

            string targetHost = _o.Sni ?? _o.Server ?? "radius.local";
            var clientCerts = _tlsCtx.ClientCert != null ? new X509CertificateCollection { _tlsCtx.ClientCert } : null;

            var authTask = ssl.AuthenticateAsClientAsync(targetHost, clientCerts, _tlsCtx.GetProtocols(), false);
            byte curEapId = serverEap.Identifier;

            // TLS ハンドシェイクの送受信ループ
            while (!authTask.IsCompleted)
            {
                byte[] outgoingTls = vStream.ExtractOutgoingTls();
                int retries = 0;
                while (outgoingTls.Length == 0 && !authTask.IsCompleted && retries++ < 50)
                {
                    Thread.Sleep(20);
                    outgoingTls = vStream.ExtractOutgoingTls();
                }

                if (outgoingTls.Length > 0)
                {
                    Progress.Log("TLS", PState.Ok, $"TLS レコードペイロード ({outgoingTls.Length} バイト) を EAP 経由で送信", "Supplicant --> RADIUS");
                    var nextEap = SendTlsPayload(curEapId, outgoingTls);
                    if (nextEap == null)
                    {
                        Progress.Log("EAP", PState.Fail, "EAP TLS ペイロードの交換に失敗しました。", "Supplicant <-- RADIUS");
                        return 4;
                    }

                    if (nextEap.Code == 3)
                    {
                        Progress.Log("RESULT", PState.Ok, "認証成功: Access-Accept (EAP-Success)", "Authenticator <-- RADIUS");
                        return 0;
                    }
                    if (nextEap.Code == 4)
                    {
                        Progress.Log("RESULT", PState.Fail, "認証拒否: Access-Reject (EAP-Failure)", "Authenticator <-- RADIUS");
                        return 1;
                    }

                    var serverTlsResult = ReceiveTlsPayload(nextEap);
                    if (serverTlsResult == null)
                    {
                        Progress.Log("TLS", PState.Fail, "サーバーからの受信 TLS レコードの再構築に失敗しました。", "Supplicant <-- RADIUS");
                        return 4;
                    }

                    curEapId = serverTlsResult.Value.NextEapId;
                    vStream.FeedIncomingTls(serverTlsResult.Value.TlsData);
                }
                else if (authTask.IsFaulted)
                {
                    break;
                }
            }

            if (authTask.IsFaulted)
            {
                Exception ex = authTask.Exception?.InnerException ?? authTask.Exception!;
                Progress.Log("TLS", PState.Fail, $"TLS ハンドシェイクエラー: {ex.Message}", "Supplicant <--> RADIUS");
                return 3;
            }

            Progress.Log("TLS", PState.Ok, $"TLS トンネル確立完了! プロトコル: {ssl.SslProtocol}, 暗号スイート: {ssl.NegotiatedCipherSuite}", "Supplicant <--> RADIUS");
            if (ssl.RemoteCertificate != null)
            {
                var sc = new X509Certificate2(ssl.RemoteCertificate);
                Progress.Info("TLS", $"サーバー証明書: {sc.Subject} (発行者: {sc.Issuer})");
            }
            if (ssl.IsMutuallyAuthenticated)
            {
                Progress.Info("TLS", "相互 TLS 確立（サーバーがクライアント証明書を受入）");
            }

            // 各方式の内部認証処理
            if (_eapMethodType == 13) // EAP-TLS
            {
                return FinishEapTls(vStream, curEapId);
            }
            else if (_eapMethodType == 25) // PEAP
            {
                return RunPeapInner(ssl, vStream, curEapId);
            }
            else if (_eapMethodType == 43) // EAP-FAST
            {
                return RunFastInner(ssl, vStream, curEapId);
            }
            else if (_eapMethodType == 55) // EAP-TEAP
            {
                return RunTeapInner(ssl, vStream, curEapId);
            }
            else // EAP-TTLS
            {
                return RunTtlsInner(ssl, vStream, curEapId);
            }
        }

        private int FinishEapTls(EapTlsTransportStream vStream, byte curEapId)
        {
            byte[] finalTls = vStream.ExtractOutgoingTls();
            if (finalTls.Length > 0)
            {
                var finalEap = SendTlsPayload(curEapId, finalTls);
                if (finalEap != null)
                {
                    if (finalEap.Code == 3)
                    {
                        Progress.Log("RESULT", PState.Ok, "EAP-TLS 認証成功: Access-Accept", "Authenticator <-- RADIUS");
                        return 0;
                    }
                    if (finalEap.Code == 4)
                    {
                        Progress.Log("RESULT", PState.Fail, "EAP-TLS 認証拒否: Access-Reject", "Authenticator <-- RADIUS");
                        return 1;
                    }
                    // FreeRADIUS 等の空 EAP-Request/TLS (Code 1) によるハンドシェイク完了 ACK 要求への対応
                    if (finalEap.Code == 1)
                    {
                        var ack = new EapPacket { Code = 2, Identifier = finalEap.Identifier, Type = 13, Data = new byte[] { 0x00 } };
                        var rResp = ExchangeRadius(BuildRadiusEapRequest(ack.Encode()));
                        if (rResp != null && rResp.Code == 2)
                        {
                            Progress.Log("RESULT", PState.Ok, "EAP-TLS 認証成功: Access-Accept", "Authenticator <-- RADIUS");
                            return 0;
                        }
                        if (rResp != null && rResp.Code == 3)
                        {
                            Progress.Log("RESULT", PState.Fail, "EAP-TLS 認証拒否: Access-Reject", "Authenticator <-- RADIUS");
                            return 1;
                        }
                    }
                }
            }
            else
            {
                var ack = new EapPacket { Code = 2, Identifier = curEapId, Type = 13, Data = new byte[] { 0x00 } };
                var rResp = ExchangeRadius(BuildRadiusEapRequest(ack.Encode()));
                if (rResp != null && rResp.Code == 2)
                {
                    Progress.Log("RESULT", PState.Ok, "EAP-TLS 認証成功: Access-Accept", "Authenticator <-- RADIUS");
                    return 0;
                }
            }
            Progress.Log("RESULT", PState.Fail, "EAP-TLS 完了 (Access-Accept 未受信)", "Authenticator <-- RADIUS");
            return 1;
        }

        private static byte[] ReadTlsChunk(SslStream ssl)
        {
            byte[] buf = new byte[4096];
            int r = ssl.Read(buf, 0, buf.Length);
            if (r <= 0) throw new EndOfStreamException();
            byte[] res = new byte[r];
            Buffer.BlockCopy(buf, 0, res, 0, r);
            return res;
        }
    }
}
