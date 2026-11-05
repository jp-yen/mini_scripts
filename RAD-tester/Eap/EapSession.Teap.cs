// EapSession.Teap.cs - EAP-TEAP 内部認証 (RFC 7170)

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
        // EAP-TEAP 内部認証 (RFC 7170)
        // --------------------------------------------------------------
        private int RunTeapInner(SslStream ssl, EapTlsTransportStream vStream, byte curEapId)
        {
            Progress.Log("BUILD", PState.Ok, $"EAP-TEAP 内部認証を開始します: ユーザー=\"{_o.User}\"...");
            byte lastServerEapId = curEapId;
            bool innerSuccess = false;
            string? failureReason = null;

            var innerTask = Task.Run(() =>
            {
                try
                {
                    byte[] buf = new byte[4096];
                    while (!innerSuccess)
                    {
                        int n = ssl.Read(buf, 0, buf.Length);
                        if (n <= 0) break;

                        var tlvs = ParseFastTlvs(buf.AsSpan(0, n).ToArray());
                        using var respStream = new MemoryStream();
                        bool replied = false;

                        foreach (var tlv in tlvs)
                        {
                            // TLV Type 17: Identity-Type TLV (1 = User, 2 = Machine)
                            if (tlv.Type == 17)
                            {
                                int idType = tlv.Value.Length >= 2 ? ((tlv.Value[0] << 8) | tlv.Value[1]) : 0;
                                string idTypeName = idType == 2 ? "マシン" : "ユーザー";
                                Progress.Log("TEAP", PState.Ok, $"サーバー要求 Identity-Type: {idTypeName}");
                            }
                            // TLV Type 9: EAP-Payload TLV
                            else if (tlv.Type == 9)
                            {
                                var inEap = EapPacket.Parse(tlv.Value, 0, tlv.Value.Length);
                                if (inEap != null)
                                {
                                    if (inEap.Code == 1) // EAP-Request
                                    {
                                        Progress.Log("TEAP", PState.Ok, $"内部 EAP-Request 受信 id={inEap.Identifier} タイプ={EapPacket.TypeName(inEap.Type)}");
                                        if (inEap.Type == 1) // Identity
                                        {
                                            Progress.Log("TEAP", PState.Ok, $"内部 EAP-Response/Identity を送信: ユーザー=\"{_o.User}\"");
                                            var innerIdResp = new EapPacket
                                            {
                                                Code = 2,
                                                Identifier = inEap.Identifier,
                                                Type = 1,
                                                Data = Encoding.UTF8.GetBytes(_o.User!)
                                            };
                                            // Identity-Type TLV (User = 1): Type 2 (FreeRADIUS 辞書) & Type 17 (RFC 7170)
                                            WriteFastTlv(respStream, 2, new byte[] { 0x00, 0x01 }, mandatory: false);
                                            WriteFastTlv(respStream, 17, new byte[] { 0x00, 0x01 }, mandatory: false);
                                            WriteFastTlv(respStream, 9, innerIdResp.Encode(), mandatory: true);
                                            replied = true;
                                        }
                                        else if (inEap.Type == 6) // EAP-GTC
                                        {
                                            string prompt = inEap.Data != null ? Encoding.UTF8.GetString(inEap.Data) : "";
                                            Progress.Log("TEAP", PState.Ok, $"内部 GTC プロンプト: \"{prompt}\"");
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

                                                Progress.Log("TEAP", PState.Ok, "内部 EAP-MSCHAPv2 レスポンスを送信");
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
                                            else if (opCode == 3) // サーバー認証成功
                                            {
                                                string succStr = inEap.Data.Length > 4 ? Encoding.ASCII.GetString(inEap.Data, 4, inEap.Data.Length - 4) : "";
                                                Progress.Log("TEAP", PState.Ok, $"内部 MS-CHAPv2 サーバー認証成功: \"{succStr}\"");

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
                            // TLV Type 13: Basic-Password-Auth-Req TLV
                            else if (tlv.Type == 13)
                            {
                                string prompt = Encoding.UTF8.GetString(tlv.Value);
                                Progress.Log("TEAP", PState.Ok, $"Basic-Password-Auth-Req プロンプト: \"{prompt}\"");
                                byte[] uBytes = Encoding.UTF8.GetBytes(_o.User!);
                                byte[] pBytes = Encoding.UTF8.GetBytes(_o.Pass ?? "");
                                if (uBytes.Length <= 255 && pBytes.Length <= 255)
                                {
                                    byte[] resp = new byte[2 + uBytes.Length + pBytes.Length];
                                    resp[0] = (byte)uBytes.Length;
                                    Buffer.BlockCopy(uBytes, 0, resp, 1, uBytes.Length);
                                    resp[1 + uBytes.Length] = (byte)pBytes.Length;
                                    Buffer.BlockCopy(pBytes, 0, resp, 2 + uBytes.Length, pBytes.Length);
                                    WriteFastTlv(respStream, 2, new byte[] { 0x00, 0x01 }, mandatory: false);
                                    WriteFastTlv(respStream, 17, new byte[] { 0x00, 0x01 }, mandatory: false);
                                    WriteFastTlv(respStream, 14, resp, mandatory: true);
                                    replied = true;
                                }
                            }
                            // TLV Type 10: Intermediate-Result TLV
                            else if (tlv.Type == 10 && tlv.Value.Length >= 2)
                            {
                                ushort res = (ushort)((tlv.Value[0] << 8) | tlv.Value[1]);
                                if (res == 1) // 成功
                                {
                                    Progress.Log("TEAP", PState.Ok, "中間結果 (Intermediate-Result) TLV: 成功");
                                    byte[] val = new byte[] { 0x00, 0x01 };
                                    WriteFastTlv(respStream, 10, val, mandatory: true);
                                    replied = true;
                                }
                                else
                                {
                                    Progress.Log("TEAP", PState.Fail, $"中間結果 (Intermediate-Result) TLV: 失敗 ({res})");
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
                                    Progress.Log("TEAP", PState.Ok, "最終結果 (Result) TLV: 成功");
                                    innerSuccess = true;
                                    byte[] val = new byte[] { 0x00, 0x01 };
                                    WriteFastTlv(respStream, 3, val, mandatory: true);
                                    replied = true;
                                    break;
                                }
                                else
                                {
                                    Progress.Log("TEAP", PState.Fail, $"最終結果 (Result) TLV: 失敗 ({res})");
                                    failureReason = $"Result TLV failure {res}";
                                    break;
                                }
                            }
                            // TLV Type 12: Crypto-Binding TLV
                            else if (tlv.Type == 12 && tlv.Value.Length >= 4)
                            {
                                Progress.Log("TEAP", PState.Ok, "Crypto-Binding TLV を受信しました。Crypto-Binding レスポンスを送信");
                                byte[] bindingResp = new byte[tlv.Value.Length];
                                Buffer.BlockCopy(tlv.Value, 0, bindingResp, 0, tlv.Value.Length);
                                bindingResp[3] = (byte)((tlv.Value[3] & 0xF0) | 0x01); // Flags (上位 4 ビット) + Response SubType (下位 4 ビット)
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
                    if (nextEap.Code == 3) { Progress.Log("RESULT", PState.Ok, "EAP-TEAP 認証成功: Access-Accept", "Authenticator <-- RADIUS"); return 0; }
                    if (nextEap.Code == 4) { Progress.Log("RESULT", PState.Fail, "EAP-TEAP 認証拒否: Access-Reject", "Authenticator <-- RADIUS"); return 1; }

                    var rx = ReceiveTlsPayload(nextEap);
                    if (rx != null)
                    {
                        lastServerEapId = rx.Value.NextEapId;
                        vStream.FeedIncomingTls(rx.Value.TlsData);
                    }
                }
                else
                {
                    var ack = new EapPacket { Code = 2, Identifier = lastServerEapId, Type = 55, Data = new byte[] { 0x01 } };
                    var rResp = ExchangeRadius(BuildRadiusEapRequest(ack.Encode()));
                    if (rResp == null) break;
                    if (rResp.Code == 2) { Progress.Log("RESULT", PState.Ok, "EAP-TEAP 認証成功: Access-Accept", "Authenticator <-- RADIUS"); return 0; }
                    if (rResp.Code == 3) { Progress.Log("RESULT", PState.Fail, "EAP-TEAP 認証拒否: Access-Reject", "Authenticator <-- RADIUS"); return 1; }

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

            innerTask.Wait(TimeSpan.FromSeconds(2));
            if (innerSuccess)
            {
                byte[] finalTls = vStream.ExtractOutgoingTls();
                if (finalTls.Length > 0)
                {
                    var fEap = SendTlsPayload(lastServerEapId, finalTls);
                    if (fEap != null && fEap.Code == 3)
                    {
                        Progress.Log("RESULT", PState.Ok, "EAP-TEAP 認証成功: Access-Accept", "Authenticator <-- RADIUS");
                        return 0;
                    }
                }
                else
                {
                    var ack = new EapPacket { Code = 2, Identifier = lastServerEapId, Type = 55, Data = new byte[] { 0x01 } };
                    var rResp = ExchangeRadius(BuildRadiusEapRequest(ack.Encode()));
                    if (rResp != null && rResp.Code == 2)
                    {
                        Progress.Log("RESULT", PState.Ok, "EAP-TEAP 認証成功: Access-Accept", "Authenticator <-- RADIUS");
                        return 0;
                    }
                }

                Progress.Log("RESULT", PState.Ok, "EAP-TEAP 認証成功: Access-Accept", "Authenticator <-- RADIUS");
                return 0;
            }

            Progress.Log("RESULT", PState.Fail, $"EAP-TEAP 認証失敗{(failureReason != null ? ": " + failureReason : "")}", "Authenticator <-- RADIUS");
            return 1;
        }
    }
}
