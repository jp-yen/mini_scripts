// PacketAnalyzer.cs - RADIUS および EAP パケットの詳細解析・要約サマリ生成

using System;
using System.Text;

namespace RadiusTest
{
    /// <summary>
    /// RADIUS / EAP / TLS パケットの内容を解析し、人間がひと目で理解できるサマリ文字列を生成します。
    /// </summary>
    public static class PacketAnalyzer
    {
        public static string Summarize(RadiusPacket p)
        {
            // 1. RADIUS 最終コード
            if (p.Code == 2) return "Access-Accept (認証成功)";
            if (p.Code == 3) return "Access-Reject (認証拒否)";
            if (p.Code == 12) return "Status-Server (生存確認)";

            // 2. EAP メッセージ属性 (Type 79) の解析
            byte[] eap = p.Concat(79);
            if (eap.Length >= 4)
            {
                byte code = eap[0];
                byte id = eap[1];
                int len = (eap[2] << 8) | eap[3];
                byte type = eap.Length >= 5 ? eap[4] : (byte)0;

                string codeName = code switch
                {
                    1 => "Request",
                    2 => "Response",
                    3 => "Success",
                    4 => "Failure",
                    _ => $"Code {code}"
                };

                if (code == 3) return "Access-Accept (EAP-Success: 成功)";
                if (code == 4) return "Access-Reject (EAP-Failure: 失敗)";

                // EAP-Identity
                if (type == 1)
                {
                    int maxLen = Math.Max(0, eap.Length - 5);
                    int desiredLen = Math.Max(0, len - 5);
                    int uLen = Math.Min(maxLen, desiredLen);
                    string user = uLen > 0 ? Encoding.UTF8.GetString(eap, 5, uLen) : "";
                    return code == 1
                        ? "Access-Challenge (EAP: 身元要求 Identity-Request)"
                        : $"Access-Request (EAP: 初期身元提示 Identity: \"{user}\")";
                }

                // EAP-Notification
                if (type == 2) return "Access-Challenge (EAP-Notification: 通知)";

                // EAP-NAK (希望方式の逆提案)
                if (type == 3)
                {
                    byte reqType = eap.Length > 5 ? eap[5] : (byte)0;
                    return $"Access-Request (EAP-NAK: 希望方式「{EapPacket.TypeName(reqType)}」要求)";
                }

                // EAP-MD5
                if (type == 4) return "Access-Challenge (EAP-MD5-Challenge チャレンジ)";

                // EAP-GTC (OTP / パスワード)
                if (type == 6)
                {
                    return code == 1
                        ? "Access-Challenge (EAP-GTC チャレンジ要求)"
                        : "Access-Request (EAP-GTC パスワード応答)";
                }

                // トンネル型 EAP (EAP-TLS, EAP-TTLS, PEAP, EAP-FAST, EAP-TEAP)
                if (type == 13 || type == 21 || type == 25 || type == 43 || type == 55)
                {
                    string mName = EapPacket.TypeName(type);
                    if (eap.Length >= 6)
                    {
                        byte flags = eap[5];
                        bool lengthIncluded = (flags & 0x80) != 0;
                        bool moreFragments = (flags & 0x40) != 0;
                        bool startBit = (flags & 0x20) != 0;

                        if (startBit)
                        {
                            return $"Access-Challenge ({mName}: TLS 開始要求 Start)";
                        }

                        int tlsOffset = lengthIncluded ? 10 : 6;
                        if (eap.Length > tlsOffset)
                        {
                            byte contentType = eap[tlsOffset];
                            // TLS Handshake (ContentType 22)
                            if (contentType == 22 && eap.Length >= tlsOffset + 6)
                            {
                                byte hsType = eap[tlsOffset + 5];
                                string hsName = hsType switch
                                {
                                    1 => "TLS Client Hello",
                                    2 => "TLS Server Hello",
                                    11 => "TLS サーバー証明書 (Certificate)",
                                    12 => "TLS Server Key Exchange",
                                    13 => "TLS Certificate Request",
                                    14 => "TLS Server Hello Done",
                                    15 => "TLS Certificate Verify",
                                    16 => "TLS Client Key Exchange",
                                    20 => "TLS Finished (トンネル確立完了)",
                                    _ => $"TLS Handshake (Type {hsType})"
                                };
                                if (moreFragments) hsName += " [分割継続]";
                                return p.Code == 1
                                    ? $"Access-Request ({mName}: {hsName})"
                                    : $"Access-Challenge ({mName}: {hsName})";
                            }

                            // TLS Application Data (ContentType 23: 暗号化トンネル内 Phase 2)
                            if (contentType == 23)
                            {
                                return p.Code == 1
                                    ? $"Access-Request ({mName}: トンネル内暗号化データ Phase 2 応答)"
                                    : $"Access-Challenge ({mName}: トンネル内暗号化データ Phase 2 要求)";
                            }

                            if (contentType == 21) return $"Access-Challenge ({mName}: TLS Alert 警告/エラー)";
                            if (contentType == 20) return $"{Dict.CodeName(p.Code)} ({mName}: TLS ChangeCipherSpec)";
                        }

                        // フラグメント分割継続または空の ACK 応答
                        if (moreFragments)
                        {
                            return p.Code == 1
                                ? $"Access-Request ({mName}: TLS 分割データ送信)"
                                : $"Access-Challenge ({mName}: TLS 証明書等の分割データ受信)";
                        }
                        if (code == 2 && eap.Length == 6)
                        {
                            return $"Access-Request ({mName}: 分割データ受領 ACK)";
                        }
                    }

                    return $"{Dict.CodeName(p.Code)} ({mName}: EAP-{codeName})";
                }

                return $"{Dict.CodeName(p.Code)} (EAP-{EapPacket.TypeName(type)} {codeName})";
            }

            // 3. 非 EAP RADIUS パケット
            if (p.Code == 1)
            {
                if (p.Get(2) != null) return "Access-Request (PAP 認証)";
                if (p.Get(3) != null) return "Access-Request (CHAP 認証)";
                if (HasMsChap(p)) return "Access-Request (MS-CHAPv2 認証)";
                if (p.Get(24) != null) return "Access-Request (State 対話応答)";
                return "Access-Request (認証リクエスト)";
            }

            if (p.Code == 11)
            {
                byte[]? replyMsg = p.Get(18);
                if (replyMsg != null)
                {
                    string msg = Encoding.UTF8.GetString(replyMsg).Trim();
                    return $"Access-Challenge (プロンプト: \"{msg}\")";
                }
                return "Access-Challenge (認証チャレンジ)";
            }

            return Dict.CodeName(p.Code);
        }

        private static bool HasMsChap(RadiusPacket p)
        {
            foreach (var a in p.Attrs)
            {
                if (a.Type == 26 && a.Value.Length >= 6)
                {
                    uint vendor = (uint)((a.Value[0] << 24) | (a.Value[1] << 16) | (a.Value[2] << 8) | a.Value[3]);
                    if (vendor == 311) return true;
                }
            }
            return false;
        }
    }
}
