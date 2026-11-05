// EapSession.Ttls.cs - EAP-TTLS 内部認証 (RFC 5281)

using System;
using System.IO;
using System.Net.Security;
using System.Security.Cryptography;
using System.Text;

namespace RadiusTest
{
    public partial class EapSession
    {
        // --------------------------------------------------------------
        // EAP-TTLS 内部認証 (RFC 5281 / Diameter AVP: PAP, MS-CHAPv2, CHAP)
        // --------------------------------------------------------------
        private int RunTtlsInner(SslStream ssl, EapTlsTransportStream vStream, byte curEapId)
        {
            string innerMethod = (_o.TtlsInner ?? "pap").ToLowerInvariant();
            Progress.Log("BUILD", PState.Ok, $"EAP-TTLS 内部 {innerMethod.ToUpperInvariant()} 認証を開始します: ユーザー=\"{_o.User}\"...", "Supplicant --> RADIUS");

            byte[] avpUser = CreateAvp(1, Encoding.UTF8.GetBytes(_o.User!));
            byte[] innerPayload;

            if (innerMethod == "chap")
            {
                // RFC 2865 / RFC 1994: CHAP-Challenge (Type 60) + CHAP-Password (Type 3)
                byte[] chapChallenge = new byte[16];
                RandomNumberGenerator.Fill(chapChallenge);
                byte[] chapPassword = Crypto.GenerateChapPassword(curEapId, _o.Pass ?? "", chapChallenge);

                byte[] avpChallenge = CreateAvp(60, chapChallenge);
                byte[] avpPass = CreateAvp(3, chapPassword);

                innerPayload = new byte[avpUser.Length + avpChallenge.Length + avpPass.Length];
                Buffer.BlockCopy(avpUser, 0, innerPayload, 0, avpUser.Length);
                Buffer.BlockCopy(avpChallenge, 0, innerPayload, avpUser.Length, avpChallenge.Length);
                Buffer.BlockCopy(avpPass, 0, innerPayload, avpUser.Length + avpChallenge.Length, avpPass.Length);
            }
            else if (innerMethod == "mschapv2")
            {
                // RFC 2548 / RFC 2759: MS-CHAP-Challenge (311/11) + MS-CHAP2-Response (311/25)
                byte[] authChallenge = new byte[16];
                RandomNumberGenerator.Fill(authChallenge);
                byte[] peerChallenge = new byte[16];
                RandomNumberGenerator.Fill(peerChallenge);

                byte[] ntResponse = Crypto.GenerateMsChap2Response(_o.User!, _o.Pass ?? "", authChallenge, peerChallenge);

                byte[] msChap2Resp = new byte[50];
                msChap2Resp[0] = curEapId; // Ident
                msChap2Resp[1] = 0;        // Flags
                Buffer.BlockCopy(peerChallenge, 0, msChap2Resp, 2, 16);
                Buffer.BlockCopy(ntResponse, 0, msChap2Resp, 26, 24);

                byte[] avpChallenge = CreateVendorAvp(311, 11, authChallenge);
                byte[] avpResponse = CreateVendorAvp(311, 25, msChap2Resp);

                innerPayload = new byte[avpUser.Length + avpChallenge.Length + avpResponse.Length];
                Buffer.BlockCopy(avpUser, 0, innerPayload, 0, avpUser.Length);
                Buffer.BlockCopy(avpChallenge, 0, innerPayload, avpUser.Length, avpChallenge.Length);
                Buffer.BlockCopy(avpResponse, 0, innerPayload, avpUser.Length + avpChallenge.Length, avpResponse.Length);
            }
            else // 既定: PAP
            {
                byte[] avpPass = CreateAvp(2, Encoding.UTF8.GetBytes(_o.Pass ?? ""));
                innerPayload = new byte[avpUser.Length + avpPass.Length];
                Buffer.BlockCopy(avpUser, 0, innerPayload, 0, avpUser.Length);
                Buffer.BlockCopy(avpPass, 0, innerPayload, avpUser.Length, avpPass.Length);
            }

            ssl.Write(innerPayload, 0, innerPayload.Length);
            ssl.Flush();

            byte[] encryptedInnerTls = vStream.ExtractOutgoingTls();
            var innerEapResult = SendTlsPayload(curEapId, encryptedInnerTls);

            if (innerEapResult != null)
            {
                if (innerEapResult.Code == 3)
                {
                    Progress.Log("RESULT", PState.Ok, "EAP-TTLS 認証成功: Access-Accept", "Authenticator <-- RADIUS");
                    return 0;
                }
                if (innerEapResult.Code == 4)
                {
                    Progress.Log("RESULT", PState.Fail, "EAP-TTLS 認証拒否: Access-Reject", "Authenticator <-- RADIUS");
                    return 1;
                }

                var ack = new EapPacket { Code = 2, Identifier = innerEapResult.Identifier, Type = 21, Data = new byte[] { 0x00 } };
                var rResp = ExchangeRadius(BuildRadiusEapRequest(ack.Encode()));
                if (rResp != null && rResp.Code == 2)
                {
                    Progress.Log("RESULT", PState.Ok, "EAP-TTLS 認証成功: Access-Accept", "Authenticator <-- RADIUS");
                    return 0;
                }
            }

            Progress.Log("RESULT", PState.Fail, "EAP-TTLS 認証失敗: Access-Reject", "Authenticator <-- RADIUS");
            return 1;
        }
    }
}
