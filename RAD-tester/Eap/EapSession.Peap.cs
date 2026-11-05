// EapSession.Peap.cs - PEAP 内部 MS-CHAPv2 認証

using System;
using System.IO;
using System.Net.Security;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RadiusTest
{
    public partial class EapSession
    {
        // --------------------------------------------------------------
        // PEAP 内部 MS-CHAPv2 認証 (MS-PEAP / PEAPv0 / RFC 2759)
        // --------------------------------------------------------------
        private int RunPeapInner(SslStream ssl, EapTlsTransportStream vStream, byte curEapId)
        {
            string peapDesc = _o.Eap == "peapv1" ? "PEAPv1 (Cisco / EAP-GTC)" : "PEAPv0 (MS / MS-CHAPv2)";
            Progress.Log("BUILD", PState.Ok, $"{peapDesc} 内部認証を開始します: ユーザー=\"{_o.User}\"...");

            bool innerSuccess = false;
            bool phase2Completed = false;
            bool gtcSent = false;
            string? expectedAuthResp = null;
            string? failureReason = null;
            byte lastServerEapId = curEapId;

            // SslStream 上の内部 EAP プロトコルをバックグラウンドタスクで実行
            var innerTask = Task.Run(() =>
            {
                try
                {
                    while (true)
                    {
                        byte[] chunk = ReadTlsChunk(ssl);
                        if (chunk.Length == 0) break;

                        // ケース 1: サーバーが EAP-Request / Identity を送信 (Code=1, Type=1)
                        if (chunk[0] == 1 && chunk.Length >= 5 && chunk[4] == 1)
                        {
                            byte id = chunk[1];
                            Progress.Log("PEAP", PState.Ok, $"内部 EAP-Request 受信 id={id} タイプ=Identity");
                            Progress.Log("PEAP", PState.Ok, $"内部 PEAPv0 Identity 応答を送信: ユーザー=\"{_o.User}\"");

                            // MS-PEAP (PEAPv0) では Identity レスポンスは 4 バイトヘッダなしで直接 Type=1 から開始
                            byte[] userBytes = Encoding.UTF8.GetBytes(_o.User!);
                            byte[] idRespData = new byte[1 + userBytes.Length];
                            idRespData[0] = 1; // Type: Identity
                            Buffer.BlockCopy(userBytes, 0, idRespData, 1, userBytes.Length);
                            ssl.Write(idRespData, 0, idRespData.Length);
                            ssl.Flush();
                        }
                        // ケース 2a: サーバーが内部 EAP-GTC を送信 (Type=6)
                        else if (chunk[0] == 6 && chunk.Length >= 1)
                        {
                            string prompt = chunk.Length > 1 ? Encoding.UTF8.GetString(chunk, 1, chunk.Length - 1) : "";
                            Progress.Log("PEAP", PState.Ok, $"内部 EAP-GTC チャレンジ: \"{prompt}\"");

                            byte[] passBytes = Encoding.UTF8.GetBytes(_o.Pass ?? "");
                            byte[] gtcRespData = new byte[1 + passBytes.Length];
                            gtcRespData[0] = 6; // Type: GTC
                            Buffer.BlockCopy(passBytes, 0, gtcRespData, 1, passBytes.Length);

                            Progress.Log("PEAP", PState.Ok, "内部 EAP-GTC レスポンスを送信");
                            ssl.Write(gtcRespData, 0, gtcRespData.Length);
                            ssl.Flush();
                            gtcSent = true;
                        }
                        // ケース 2b: サーバーが EAP-MSCHAPv2 を送信 (Type=26)
                        else if (chunk[0] == 26 && chunk.Length >= 5)
                        {
                            byte opCode = chunk[1];
                            byte mschapId = chunk[2];
                            int msLen = (chunk[3] << 8) | chunk[4];

                            if (opCode == 1) // チャレンジ
                            {
                                if (chunk.Length < 6) break;
                                int valSize = chunk[5];
                                if (valSize < 16 || chunk.Length < 6 + valSize) break;

                                Progress.Log("PEAP", PState.Ok, $"内部 EAP-MSCHAPv2 チャレンジ受信 (id={mschapId}, 長さ={msLen})");

                                byte[] authChallenge = new byte[16];
                                Buffer.BlockCopy(chunk, 6, authChallenge, 0, 16);

                                byte[] peerChallenge = new byte[16];
                                RandomNumberGenerator.Fill(peerChallenge);

                                byte[] ntResponse = Crypto.GenerateMsChap2Response(_o.User!, _o.Pass!, authChallenge, peerChallenge);
                                expectedAuthResp = Crypto.GenerateAuthenticatorResponse(_o.Pass ?? "", ntResponse, peerChallenge, authChallenge, _o.User ?? "");

                                byte[] userBytes = Encoding.UTF8.GetBytes(_o.User!);
                                int msPayloadLen = 5 + 49 + userBytes.Length;

                                byte[] respData = new byte[1 + msPayloadLen];
                                respData[0] = 26; // Type: EAP-MSCHAPv2
                                respData[1] = 2;  // OpCode: Response
                                respData[2] = mschapId;
                                respData[3] = (byte)(msPayloadLen >> 8);
                                respData[4] = (byte)msPayloadLen;
                                respData[5] = 49; // Value-Size
                                Buffer.BlockCopy(peerChallenge, 0, respData, 6, 16);
                                // respData[22..29] は予約 0
                                Buffer.BlockCopy(ntResponse, 0, respData, 30, 24);
                                respData[54] = 0; // Flags
                                Buffer.BlockCopy(userBytes, 0, respData, 55, userBytes.Length);

                                Progress.Log("PEAP", PState.Ok, "内部 EAP-MSCHAPv2 レスポンスを送信 (NT-Response 24 バイト)");
                                ssl.Write(respData, 0, respData.Length);
                                ssl.Flush();
                            }
                            else if (opCode == 3) // サーバー認証成功
                            {
                                int strLen = Math.Max(0, Math.Min(msLen - 5, chunk.Length - 5));
                                string succStr = strLen > 0 ? Encoding.ASCII.GetString(chunk, 5, strLen) : "";

                                // MS-CHAPv2 相互認証の検証 (RFC 2759)
                                bool mutualAuthOk = false;
                                if (!string.IsNullOrEmpty(expectedAuthResp) && !string.IsNullOrEmpty(succStr))
                                {
                                    int sIdx = succStr.IndexOf("S=", StringComparison.OrdinalIgnoreCase);
                                    if (sIdx >= 0 && succStr.Length >= sIdx + 2 + 40)
                                    {
                                        string serverS = succStr.Substring(sIdx + 2, 40);
                                        mutualAuthOk = string.Equals(serverS, expectedAuthResp, StringComparison.OrdinalIgnoreCase);
                                    }
                                }

                                if (!mutualAuthOk)
                                {
                                    Progress.Log("PEAP", PState.Fail, $"サーバー相互認証の検証に失敗しました (偽サーバーの可能性): AuthenticatorResponse 不一致 (受信=\"{succStr}\", 期待=\"S={expectedAuthResp}\")");
                                    failureReason = "MS-CHAPv2 mutual authentication failed";
                                    break;
                                }

                                Progress.Log("PEAP", PState.Ok, $"内部 MS-CHAPv2 サーバー認証成功 (相互認証一致: \"{succStr}\")");
                                phase2Completed = true;

                                // 成功 ACK を返信 (OpCode 3)
                                byte[] ackData = new byte[5];
                                ackData[0] = 26; // Type: EAP-MSCHAPv2
                                ackData[1] = 3;  // OpCode: Success ACK
                                ackData[2] = mschapId;
                                ackData[3] = 0;
                                ackData[4] = 4;
                                ssl.Write(ackData, 0, ackData.Length);
                                ssl.Flush();
                            }
                            else if (opCode == 4) // 認証失敗
                            {
                                int strLen = Math.Max(0, Math.Min(msLen - 5, chunk.Length - 5));
                                string errStr = strLen > 0 ? Encoding.ASCII.GetString(chunk, 5, strLen) : "";
                                string errNote = errStr.IndexOf("E=691", StringComparison.OrdinalIgnoreCase) >= 0
                                    ? " (パスワード誤りまたはユーザーが存在しません)"
                                    : "";
                                Progress.Log("PEAP", PState.Fail, $"内部 MS-CHAPv2 認証失敗: \"{errStr}\"{errNote}");
                                failureReason = errStr;
                                break;
                            }
                        }
                        // ケース 3: サーバーが EAP-TLV Result (Type 33) を送信
                        else if (chunk[0] == 1 && chunk.Length >= 11 && chunk[4] == 33)
                        {
                            byte tlvId = chunk[1];
                            byte result = chunk[10];
                            if (result == 1) // 成功
                            {
                                // CVE-2023-52160 対策: Phase 2 認証が未完了のまま送られた Result-TLV (Success) を拒否
                                if (!phase2Completed && !gtcSent)
                                {
                                    Progress.Log("PEAP", PState.Fail, "脆弱性検知 (CVE-2023-52160 類似): Phase 2 認証が完了する前に Result-TLV (Success) を受信しました。認証を拒否します。");
                                    failureReason = "Premature Result-TLV without Phase 2 authentication (CVE-2023-52160 bypass attempt)";
                                    break;
                                }

                                Progress.Log("PEAP", PState.Ok, "内部 EAP-TLV (Success) を受信しました。Result-TLV ACK を返信");
                                byte[] tlvResp = new byte[11];
                                tlvResp[0] = 2;  // Code: Response
                                tlvResp[1] = tlvId;
                                tlvResp[2] = 0;
                                tlvResp[3] = 11; // Length: 11
                                tlvResp[4] = 33; // Type: EAP-TLV
                                tlvResp[5] = 0x80; // Mandatory
                                tlvResp[6] = 3;    // Result-TLV
                                tlvResp[7] = 0;
                                tlvResp[8] = 2;    // TLV Length: 2
                                tlvResp[9] = 0;
                                tlvResp[10] = 1;   // SUCCESS
                                ssl.Write(tlvResp, 0, 11);
                                ssl.Flush();

                                innerSuccess = true;
                                break;
                            }
                            else
                            {
                                Progress.Log("PEAP", PState.Fail, $"内部 EAP-TLV (Failure) を受信しました (result={result})");
                                failureReason = $"TLV failure code {result}";
                                break;
                            }
                        }
                        // ケース 4: 標準 EAP-Success (Code 3)
                        else if (chunk[0] == 3)
                        {
                            // CVE-2023-52160 対策: Phase 2 認証が未完了のまま送られた EAP-Success を拒否
                            if (!phase2Completed && !gtcSent)
                            {
                                Progress.Log("PEAP", PState.Fail, "脆弱性検知 (CVE-2023-52160 類似): Phase 2 認証が完了する前に EAP-Success を受信しました。認証を拒否します。");
                                failureReason = "Premature EAP-Success without Phase 2 authentication (CVE-2023-52160 bypass attempt)";
                                break;
                            }
                            innerSuccess = true;
                            break;
                        }
                        else
                        {
                            Progress.Log("PEAP", PState.Warn, $"未対応の内部 PEAP チャンクを受信しました: {BitConverter.ToString(chunk)}");
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (!innerSuccess) failureReason = ex.Message;
                }
            });

            // メインループ: SslStream と RADIUS UDP 間の TLS レコード送受信を仲介
            while (!innerTask.IsCompleted)
            {
                byte[] outgoingTls = vStream.ExtractOutgoingTls();
                int waitCount = 0;
                while (outgoingTls.Length == 0 && !innerTask.IsCompleted && waitCount++ < 30)
                {
                    Thread.Sleep(20);
                    outgoingTls = vStream.ExtractOutgoingTls();
                }

                if (outgoingTls.Length > 0)
                {
                    var nextEap = SendTlsPayload(lastServerEapId, outgoingTls);
                    if (nextEap == null) break;
                    if (nextEap.Code == 3)
                    {
                        if (!innerSuccess)
                        {
                            Progress.Log("RESULT", PState.Fail, "脆弱性検知 (CVE-2023-52160 類似): 内部 PEAP 認証が完了していない状態で EAP-Success を受信しました。認証を拒否します。", "Authenticator <-- RADIUS");
                            return 1;
                        }
                        Progress.Log("RESULT", PState.Ok, "PEAP 認証成功: Access-Accept", "Authenticator <-- RADIUS");
                        return 0;
                    }
                    if (nextEap.Code == 4) { Progress.Log("RESULT", PState.Fail, "PEAP 認証拒否: Access-Reject", "Authenticator <-- RADIUS"); return 1; }

                    var rx = ReceiveTlsPayload(nextEap);
                    if (rx != null)
                    {
                        lastServerEapId = rx.Value.NextEapId;
                        vStream.FeedIncomingTls(rx.Value.TlsData);
                    }
                }
                else
                {
                    // サーバーからの後続パケットを取得するための空 ACK
                    var ack = new EapPacket { Code = 2, Identifier = lastServerEapId, Type = 25, Data = new byte[] { 0x00 } };
                    var rResp = ExchangeRadius(BuildRadiusEapRequest(ack.Encode()));
                    if (rResp == null) break;
                    if (rResp.Code == 2)
                    {
                        if (!innerSuccess)
                        {
                            Progress.Log("RESULT", PState.Fail, "脆弱性検知 (CVE-2023-52160 類似): 内部 PEAP 認証が完了していない状態で Access-Accept を受信しました。認証を拒否します。", "Authenticator <-- RADIUS");
                            return 1;
                        }
                        Progress.Log("RESULT", PState.Ok, "PEAP 認証成功: Access-Accept", "Authenticator <-- RADIUS");
                        return 0;
                    }
                    if (rResp.Code == 3) { Progress.Log("RESULT", PState.Fail, "PEAP 認証拒否: Access-Reject", "Authenticator <-- RADIUS"); return 1; }

                    byte[] eapBytes = rResp.Concat(79);
                    var inEap = EapPacket.Parse(eapBytes, 0, eapBytes.Length);
                    if (inEap != null)
                    {
                        var rx = ReceiveTlsPayload(inEap);
                        if (rx != null)
                        {
                            lastServerEapId = rx.Value.NextEapId;
                            vStream.FeedIncomingTls(rx.Value.TlsData);
                        }
                    }
                }
            }

            if (innerSuccess)
            {
                byte[] finalTls = vStream.ExtractOutgoingTls();
                if (finalTls.Length > 0)
                {
                    var fEap = SendTlsPayload(lastServerEapId, finalTls);
                    if (fEap != null)
                    {
                        if (fEap.Code == 3)
                        {
                            Progress.Log("RESULT", PState.Ok, "PEAP 認証成功: Access-Accept", "Authenticator <-- RADIUS");
                            return 0;
                        }
                        if (fEap.Code == 4)
                        {
                            Progress.Log("RESULT", PState.Fail, "PEAP 認証拒否: Access-Reject", "Authenticator <-- RADIUS");
                            return 1;
                        }
                        if (fEap.Code == 1)
                        {
                            var ack = new EapPacket { Code = 2, Identifier = fEap.Identifier, Type = 25, Data = new byte[] { 0x00 } };
                            var rResp = ExchangeRadius(BuildRadiusEapRequest(ack.Encode()));
                            if (rResp != null && rResp.Code == 2)
                            {
                                Progress.Log("RESULT", PState.Ok, "PEAP 認証成功: Access-Accept", "Authenticator <-- RADIUS");
                                return 0;
                            }
                            if (rResp != null && rResp.Code == 3)
                            {
                                Progress.Log("RESULT", PState.Fail, "PEAP 認証拒否: Access-Reject", "Authenticator <-- RADIUS");
                                return 1;
                            }
                        }
                    }
                }
                else
                {
                    var ack = new EapPacket { Code = 2, Identifier = lastServerEapId, Type = 25, Data = new byte[] { 0x00 } };
                    var rResp = ExchangeRadius(BuildRadiusEapRequest(ack.Encode()));
                    if (rResp != null && rResp.Code == 2)
                    {
                        Progress.Log("RESULT", PState.Ok, "PEAP 認証成功: Access-Accept", "Authenticator <-- RADIUS");
                        return 0;
                    }
                }

                Progress.Log("RESULT", PState.Ok, "PEAP 認証成功: Access-Accept", "Authenticator <-- RADIUS");
                return 0;
            }

            Progress.Log("RESULT", PState.Fail, $"PEAP 認証失敗{(failureReason != null ? ": " + failureReason : "")}", "Authenticator <-- RADIUS");
            return 1;
        }
    }
}
