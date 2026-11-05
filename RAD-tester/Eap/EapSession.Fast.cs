// EapSession.Fast.cs - EAP-FAST 内部認証 (RFC 4851)

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
        // EAP-FAST 内部認証 (RFC 4851)
        // --------------------------------------------------------------
        private int RunFastInner(SslStream ssl, EapTlsTransportStream vStream, byte curEapId)
        {
            Progress.Log("BUILD", PState.Ok, $"EAP-FAST 内部認証を開始します: ユーザー=\"{_o.User}\"...");

            bool innerSuccess = false;
            string? failureReason = null;
            byte lastServerEapId = curEapId;

            var innerTask = Task.Run(() =>
            {
                try
                {
                    while (true)
                    {
                        byte[] chunk = ReadTlsChunk(ssl);
                        if (chunk.Length == 0) break;

                        var tlvs = ParseFastTlvs(chunk);
                        bool replied = false;

                        using var respStream = new MemoryStream();

                        foreach (var tlv in tlvs)
                        {
                            // TLV Type 9: EAP-Payload TLV (RFC 3748 EAP パケットを内包)
                            if (tlv.Type == 9)
                            {
                                var inEap = EapPacket.Parse(tlv.Value, 0, tlv.Value.Length);
                                if (inEap != null)
                                {
                                    Progress.Log("FAST", PState.Ok, $"内部 EAP-{EapPacket.CodeName(inEap.Code)} id={inEap.Identifier} タイプ={EapPacket.TypeName(inEap.Type)}");

                                    if (inEap.Code == 1) // Request
                                    {
                                        if (inEap.Type == 1) // Identity
                                        {
                                            Progress.Log("FAST", PState.Ok, $"内部 EAP-Response/Identity を送信: ユーザー=\"{_o.User}\"");
                                            var innerIdResp = new EapPacket
                                            {
                                                Code = 2,
                                                Identifier = inEap.Identifier,
                                                Type = 1,
                                                Data = Encoding.UTF8.GetBytes(_o.User!)
                                            };
                                            WriteFastTlv(respStream, 9, innerIdResp.Encode(), mandatory: true);
                                            replied = true;
                                        }
                                        else if (inEap.Type == 6) // EAP-GTC
                                        {
                                            string prompt = Encoding.UTF8.GetString(inEap.Data);
                                            Progress.Log("FAST", PState.Ok, $"内部 EAP-GTC チャレンジ: \"{prompt}\"");
                                            Progress.Log("FAST", PState.Ok, "内部 EAP-GTC レスポンスを送信");
                                            var gtcResp = new EapPacket
                                            {
                                                Code = 2,
                                                Identifier = inEap.Identifier,
                                                Type = 6,
                                                Data = Encoding.UTF8.GetBytes(_o.Pass ?? "")
                                            };
                                            WriteFastTlv(respStream, 9, gtcResp.Encode(), mandatory: true);
                                            replied = true;
                                        }
                                        else if (inEap.Type == 26 && inEap.Data.Length >= 2) // EAP-MSCHAPv2
                                        {
                                            byte opCode = inEap.Data[0];
                                            byte mschapId = inEap.Data[1];
                                            if (opCode == 1) // チャレンジ
                                            {
                                                if (inEap.Data.Length < 21) break;
                                                byte[] authChallenge = new byte[16];
                                                Buffer.BlockCopy(inEap.Data, 5, authChallenge, 0, 16);
                                                byte[] peerChallenge = new byte[16];
                                                RandomNumberGenerator.Fill(peerChallenge);
                                                byte[] ntResponse = Crypto.GenerateMsChap2Response(_o.User!, _o.Pass!, authChallenge, peerChallenge);

                                                byte[] userBytes = Encoding.UTF8.GetBytes(_o.User!);
                                                int msLen = 5 + 49 + userBytes.Length;
                                                byte[] respData = new byte[msLen];
                                                respData[0] = 2; // OpCode: Response
                                                respData[1] = mschapId;
                                                respData[2] = (byte)(msLen >> 8);
                                                respData[3] = (byte)msLen;
                                                respData[4] = 49;
                                                Buffer.BlockCopy(peerChallenge, 0, respData, 5, 16);
                                                Buffer.BlockCopy(ntResponse, 0, respData, 29, 24);
                                                respData[53] = 0;
                                                Buffer.BlockCopy(userBytes, 0, respData, 54, userBytes.Length);

                                                Progress.Log("FAST", PState.Ok, "内部 EAP-MSCHAPv2 レスポンスを送信");
                                                var msResp = new EapPacket
                                                {
                                                    Code = 2,
                                                    Identifier = inEap.Identifier,
                                                    Type = 26,
                                                    Data = respData
                                                };
                                                WriteFastTlv(respStream, 9, msResp.Encode(), mandatory: true);
                                                replied = true;
                                            }
                                            else if (opCode == 3) // 成功
                                            {
                                                string succStr = inEap.Data.Length > 4 ? Encoding.ASCII.GetString(inEap.Data, 4, inEap.Data.Length - 4) : "";
                                                Progress.Log("FAST", PState.Ok, $"内部 MS-CHAPv2 サーバー認証成功: \"{succStr}\"");

                                                byte[] ackData = new byte[4] { 3, mschapId, 0, 4 };
                                                var msAck = new EapPacket
                                                {
                                                    Code = 2,
                                                    Identifier = inEap.Identifier,
                                                    Type = 26,
                                                    Data = ackData
                                                };
                                                WriteFastTlv(respStream, 9, msAck.Encode(), mandatory: true);
                                                replied = true;
                                            }
                                        }
                                    }
                                    else if (inEap.Code == 3) // EAP-Success
                                    {
                                        innerSuccess = true;
                                    }
                                    else if (inEap.Code == 4) // EAP-Failure
                                    {
                                        failureReason = "内部 EAP-Failure を受信しました。";
                                        break;
                                    }
                                }
                            }
                            // TLV Type 10: Intermediate-Result TLV
                            else if (tlv.Type == 10 && tlv.Value.Length >= 2)
                            {
                                ushort res = (ushort)((tlv.Value[0] << 8) | tlv.Value[1]);
                                if (res == 1) // 成功
                                {
                                    Progress.Log("FAST", PState.Ok, "中間結果 (Intermediate-Result) TLV: 成功");
                                    byte[] val = new byte[] { 0x00, 0x01 };
                                    WriteFastTlv(respStream, 10, val, mandatory: true);
                                    replied = true;
                                }
                                else
                                {
                                    Progress.Log("FAST", PState.Fail, $"中間結果 (Intermediate-Result) TLV: 失敗 ({res})");
                                    failureReason = $"Intermediate-Result failure {res}";
                                    break;
                                }
                            }
                            // TLV Type 3: Result TLV
                            else if (tlv.Type == 3 && tlv.Value.Length >= 2)
                            {
                                ushort res = (ushort)((tlv.Value[0] << 8) | tlv.Value[1]);
                                if (res == 1) // 成功
                                {
                                    Progress.Log("FAST", PState.Ok, "最終結果 (Result) TLV: 成功");
                                    innerSuccess = true;
                                    byte[] val = new byte[] { 0x00, 0x01 };
                                    WriteFastTlv(respStream, 3, val, mandatory: true);
                                    replied = true;
                                    break;
                                }
                                else
                                {
                                    Progress.Log("FAST", PState.Fail, $"最終結果 (Result) TLV: 失敗 ({res})");
                                    failureReason = $"Result TLV failure {res}";
                                    break;
                                }
                            }
                            // TLV Type 12: Crypto-Binding TLV
                            else if (tlv.Type == 12 && tlv.Value.Length >= 56)
                            {
                                Progress.Log("FAST", PState.Ok, "Crypto-Binding TLV を受信しました。Crypto-Binding レスポンスを送信");
                                byte[] bindingResp = new byte[tlv.Value.Length];
                                Buffer.BlockCopy(tlv.Value, 0, bindingResp, 0, tlv.Value.Length);
                                bindingResp[3] = 2; // SubType: Response
                                bindingResp[35] |= 0x01; // Nonce bit 0 = 1
                                WriteFastTlv(respStream, 12, bindingResp, mandatory: true);
                                replied = true;
                            }
                        }

                        if (replied)
                        {
                            byte[] outBytes = respStream.ToArray();
                            ssl.Write(outBytes, 0, outBytes.Length);
                            ssl.Flush();
                        }

                        if (innerSuccess) break;
                    }
                }
                catch (Exception ex)
                {
                    if (!innerSuccess) failureReason = ex.Message;
                }
            });

            // メインループ
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
                    if (nextEap.Code == 3) { Progress.Log("RESULT", PState.Ok, "EAP-FAST 認証成功: Access-Accept", "Authenticator <-- RADIUS"); return 0; }
                    if (nextEap.Code == 4) { Progress.Log("RESULT", PState.Fail, "EAP-FAST 認証拒否: Access-Reject", "Authenticator <-- RADIUS"); return 1; }

                    var rx = ReceiveTlsPayload(nextEap);
                    if (rx != null)
                    {
                        lastServerEapId = rx.Value.NextEapId;
                        vStream.FeedIncomingTls(rx.Value.TlsData);
                    }
                }
                else
                {
                    var ack = new EapPacket { Code = 2, Identifier = lastServerEapId, Type = 43, Data = new byte[] { 0x01 } };
                    var rResp = ExchangeRadius(BuildRadiusEapRequest(ack.Encode()));
                    if (rResp == null) break;
                    if (rResp.Code == 2) { Progress.Log("RESULT", PState.Ok, "EAP-FAST 認証成功: Access-Accept", "Authenticator <-- RADIUS"); return 0; }
                    if (rResp.Code == 3) { Progress.Log("RESULT", PState.Fail, "EAP-FAST 認証拒否: Access-Reject", "Authenticator <-- RADIUS"); return 1; }

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
                    if (fEap != null && fEap.Code == 3)
                    {
                        Progress.Log("RESULT", PState.Ok, "EAP-FAST 認証成功: Access-Accept", "Authenticator <-- RADIUS");
                        return 0;
                    }
                }
                else
                {
                    var ack = new EapPacket { Code = 2, Identifier = lastServerEapId, Type = 43, Data = new byte[] { 0x01 } };
                    var rResp = ExchangeRadius(BuildRadiusEapRequest(ack.Encode()));
                    if (rResp != null && rResp.Code == 2)
                    {
                        Progress.Log("RESULT", PState.Ok, "EAP-FAST 認証成功: Access-Accept", "Authenticator <-- RADIUS");
                        return 0;
                    }
                }

                Progress.Log("RESULT", PState.Ok, "EAP-FAST 認証成功: Access-Accept", "Authenticator <-- RADIUS");
                return 0;
            }

            Progress.Log("RESULT", PState.Fail, $"EAP-FAST 認証失敗{(failureReason != null ? ": " + failureReason : "")}", "Authenticator <-- RADIUS");
            return 1;
        }
    }
}
