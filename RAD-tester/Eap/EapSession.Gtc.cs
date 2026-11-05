// EapSession.Gtc.cs - 単体 EAP-GTC 認証 (RFC 3748)

using System;
using System.Text;

namespace RadiusTest
{
    public partial class EapSession
    {
        // --------------------------------------------------------------
        // 単体 EAP-GTC 認証 (RFC 3748)
        // --------------------------------------------------------------
        private int RunGtc(EapPacket serverEap)
        {
            Progress.Log("BUILD", PState.Ok, $"EAP-GTC 認証を開始: ユーザー=\"{_o.User}\"");

            string prompt = Encoding.UTF8.GetString(serverEap.Data);
            if (!string.IsNullOrEmpty(prompt))
                Progress.Log("GTC", PState.Ok, $"サーバーチャレンジプロンプト: \"{prompt}\"");

            var gtcResp = new EapPacket
            {
                Code = 2, // Response
                Identifier = serverEap.Identifier,
                Type = 6, // EAP-GTC
                Data = Encoding.UTF8.GetBytes(_o.Pass ?? "")
            };

            Progress.Log("GTC", PState.Ok, "EAP-GTC レスポンスを送信");
            var radiusReq = BuildRadiusEapRequest(gtcResp.Encode());
            var radiusResp = ExchangeRadius(radiusReq);
            if (radiusResp == null) return 4;

            if (radiusResp.Code == 2)
            {
                Progress.Log("RESULT", PState.Ok, "EAP-GTC 認証成功: Access-Accept (EAP-Success)", "Authenticator <-- RADIUS");
                return 0;
            }
            if (radiusResp.Code == 3)
            {
                Progress.Log("RESULT", PState.Fail, "EAP-GTC 認証拒否: Access-Reject (EAP-Failure)", "Authenticator <-- RADIUS");
                return 1;
            }

            byte[] eapBytes = radiusResp.Concat(79);
            var inEap = EapPacket.Parse(eapBytes, 0, eapBytes.Length);
            if (inEap != null)
            {
                if (inEap.Code == 3)
                {
                    Progress.Log("RESULT", PState.Ok, "EAP-GTC 認証成功: Access-Accept (EAP-Success)", "Authenticator <-- RADIUS");
                    return 0;
                }
                if (inEap.Code == 4)
                {
                    Progress.Log("RESULT", PState.Fail, "EAP-GTC 認証拒否: Access-Reject (EAP-Failure)", "Authenticator <-- RADIUS");
                    return 1;
                }
            }

            Progress.Log("RESULT", PState.Fail, $"EAP-GTC 認証終了: {Dict.CodeName(radiusResp.Code)}", "Authenticator <-- RADIUS");
            return 1;
        }
    }
}
