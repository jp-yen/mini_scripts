using System;
using System.Collections.Generic;
using System.Text;

namespace GpgUi
{
    /// <summary>
    /// アルゴリズム番号・文字列を人間が読める表示名に変換する共通ヘルパー。
    /// GpgKey と GpgSubkey の DisplayAlgo プロパティから呼ばれる。
    /// </summary>
    internal static class AlgoDisplayHelper
    {
        public static string GetDisplayAlgo(string algorithm, int keyLength)
        {
            string algo = algorithm ?? "";
            if (algo == "22" || algo.IndexOf("ed", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Ed25519 / Ed448";
            if (algo == "18" || algo.IndexOf("cv", StringComparison.OrdinalIgnoreCase) >= 0 || algo.IndexOf("ecdh", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Cv25519 / Cv448";
            if (algo == "19" || algo.IndexOf("ecdsa", StringComparison.OrdinalIgnoreCase) >= 0)
                return "ECDSA" + (keyLength > 0 ? "-" + keyLength : "");
            if (algo == "1" || algo == "2" || algo == "3" || algo.IndexOf("rsa", StringComparison.OrdinalIgnoreCase) >= 0)
                return "RSA" + (keyLength > 0 ? "-" + keyLength : "");
            if (algo == "17" || algo.IndexOf("dsa", StringComparison.OrdinalIgnoreCase) >= 0)
                return "DSA" + (keyLength > 0 ? "-" + keyLength : "");
            if (algo == "16" || algo.IndexOf("elg", StringComparison.OrdinalIgnoreCase) >= 0)
                return "ElGamal" + (keyLength > 0 ? "-" + keyLength : "");
            return !string.IsNullOrEmpty(algo) ? algo : "Unknown";
        }
    }

    public class GpgKey
    {
        public string KeyType { get; set; }
        public string Trust { get; set; }
        public int KeyLength { get; set; }
        public string Algorithm { get; set; }
        public string KeyId { get; set; }
        public DateTime? CreationDate { get; set; }
        public DateTime? ExpirationDate { get; set; }
        public string Fingerprint { get; set; }
        public bool IsSecretKey { get; set; }
        public List<GpgUid> Uids { get; set; }
        public List<GpgSubkey> Subkeys { get; set; }

        public GpgKey()
        {
            Uids = new List<GpgUid>();
            Subkeys = new List<GpgSubkey>();
        }

        public string PrimaryUidName
        {
            get
            {
                if (Uids != null && Uids.Count > 0)
                    return Uids[0].Name;
                return "Unknown";
            }
        }

        public string PrimaryUidEmail
        {
            get
            {
                if (Uids != null && Uids.Count > 0)
                    return Uids[0].Email;
                return "";
            }
        }

        public string DisplayTitle
        {
            get
            {
                string title = PrimaryUidName;
                if (!string.IsNullOrEmpty(PrimaryUidEmail))
                    title += " <" + PrimaryUidEmail + ">";
                return title;
            }
        }

        public string DisplayAlgo
        {
            get { return AlgoDisplayHelper.GetDisplayAlgo(Algorithm, KeyLength); }
        }

        public string DisplayKeyType
        {
            get { return IsSecretKey ? "秘密鍵" : "公開鍵"; }
        }

        public string FormattedFingerprint
        {
            get
            {
                if (string.IsNullOrEmpty(Fingerprint)) return "";
                var sb = new StringBuilder();
                for (int i = 0; i < Fingerprint.Length; i++)
                {
                    if (i > 0 && i % 4 == 0) sb.Append(" ");
                    sb.Append(Fingerprint[i]);
                }
                return sb.ToString();
            }
        }

        /// <summary>
        /// 有効な暗号化サブキーが存在するかどうかを判定する共通ロジック。
        /// IsUsableForEncryption および UnusableReason から呼び出される。
        /// </summary>
        private bool HasValidEncryptionSubkey()
        {
            if (Subkeys == null || Subkeys.Count == 0) return true; // サブキーなしの場合はチェックしない
            foreach (var sub in Subkeys)
            {
                if (sub.Capabilities != null && sub.Capabilities.Contains("e"))
                {
                    if (sub.ExpirationDate.HasValue && sub.ExpirationDate.Value < DateTime.UtcNow) continue;
                    string st = (sub.Trust ?? "").ToLower();
                    if (st == "r" || st == "e" || st == "d" || st == "i") continue;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 暗号化の受信者として使用可能かどうかを判定する。
        /// GnuPGの仕様上、暗号化に使用できる信頼レベル: u=究極信頼, f=完全信頼, m=ある程度信頼 のみ。
        /// "-" (未設定), "n" (信頼しない), "q" (未確認), "e" (期限切れ), "r" (失効), "d" (無効化), "i" (無効) は使用不可。
        /// </summary>
        public bool IsUsableForEncryption
        {
            get
            {
                // 期限切れチェック
                if (ExpirationDate.HasValue && ExpirationDate.Value < DateTime.UtcNow) return false;

                string t = (Trust ?? "").ToLower();
                // 暗号化に使用可能な信頼レベルは u, f, m のみ
                if (!(t == "u" || t == "f" || t == "m")) return false;

                // 暗号化サブキーの有無も確認（サブキーが存在する場合）
                if (Subkeys != null && Subkeys.Count > 0)
                {
                    if (!HasValidEncryptionSubkey()) return false;
                }

                return true;
            }
        }

        /// <summary>
        /// 鍵の信頼レベルを日本語で表示する。
        /// </summary>
        public string TrustDisplayText
        {
            get
            {
                string t = (Trust ?? "").ToLower();
                if (ExpirationDate.HasValue && ExpirationDate.Value < DateTime.UtcNow) return "期限切れ";
                switch (t)
                {
                    case "u": return "究極信頼";
                    case "f": return "完全信頼";
                    case "m": return "ある程度信頼";
                    case "n": return "信頼しない";
                    case "r": return "失効";
                    case "e": return "期限切れ";
                    case "d": return "無効化";
                    case "i": return "無効";
                    case "q": return "未確認";
                    case "-": return "未設定";
                    default: return "未設定";
                }
            }
        }

        /// <summary>
        /// 使用不可の理由を返す。使用可能な場合は空文字列。
        /// </summary>
        public string UnusableReason
        {
            get
            {
                if (ExpirationDate.HasValue && ExpirationDate.Value < DateTime.UtcNow)
                    return "期限切れ";
                string t = (Trust ?? "").ToLower();
                if (t == "r") return "失効";
                if (t == "e") return "期限切れ";
                if (t == "d") return "無効化";
                if (t == "i") return "無効";
                if (t == "n") return "信頼しない";
                if (t == "-" || string.IsNullOrEmpty(t) || t == "q" || t == "o") return "信頼未確定";

                if (Subkeys != null && Subkeys.Count > 0)
                {
                    if (!HasValidEncryptionSubkey()) return "暗号機能なし";
                }
                return "";
            }
        }

        /// <summary>
        /// 画面の「状態」列に表示する表示用テキスト。暗号化に利用可能な場合は「利用可能」、不可の場合はその理由を表示。
        /// </summary>
        public string StatusDisplayText
        {
            get
            {
                string reason = UnusableReason;
                return string.IsNullOrEmpty(reason) ? "利用可能" : reason;
            }
        }

        /// <summary>
        /// ISO 形式 YYYY-MM-DD で表示するための作成日プロパティ。
        /// </summary>
        public string DisplayCreationDate
        {
            get
            {
                return CreationDate.HasValue ? CreationDate.Value.ToString("yyyy-MM-dd") : "不明";
            }
        }

        /// <summary>
        /// ISO 形式 YYYY-MM-DD または「無期限」で表示するための有効期限プロパティ。
        /// </summary>
        public string DisplayExpirationDate
        {
            get
            {
                return ExpirationDate.HasValue ? ExpirationDate.Value.ToString("yyyy-MM-dd") : "無期限";
            }
        }

        /// <summary>
        /// 期限切れかどうかを判定するフラグ。
        /// </summary>
        public bool IsExpired
        {
            get
            {
                if (ExpirationDate.HasValue && ExpirationDate.Value < DateTime.UtcNow) return true;
                string t = (Trust ?? "").ToLower();
                return t == "e";
            }
        }
    }

    public class GpgUid
    {
        public string Trust { get; set; }
        public string RawUid { get; set; }
        public string Name { get; set; }
        public string Email { get; set; }
        public string Comment { get; set; }
    }

    public class GpgSubkey
    {
        public string KeyType { get; set; }
        public string Trust { get; set; }
        public int KeyLength { get; set; }
        public string Algorithm { get; set; }
        public string KeyId { get; set; }
        public DateTime? CreationDate { get; set; }
        public DateTime? ExpirationDate { get; set; }
        public string Fingerprint { get; set; }
        public string Capabilities { get; set; }

        public string DisplayAlgo
        {
            get { return AlgoDisplayHelper.GetDisplayAlgo(Algorithm, KeyLength); }
        }
    }

    public class GpgSystemInfo
    {
        public string Version { get; set; }
        public string HomeDirectory { get; set; }
        public string SupportedAlgorithms { get; set; }
        public bool IsAvailable { get; set; }

        public GpgSystemInfo()
        {
            Version = "GnuPG (Unknown)";
            HomeDirectory = "";
            SupportedAlgorithms = "";
            IsAvailable = false;
        }
    }

    public class CryptoResult
    {
        public bool Success { get; set; }
        public string OutputText { get; set; }
        public string OutputFilePath { get; set; }
        public string ErrorMessage { get; set; }
        public string SignerInfo { get; set; }

        public CryptoResult()
        {
            OutputText = "";
            OutputFilePath = "";
            ErrorMessage = "";
            SignerInfo = "";
        }
    }

    public class SignVerifyResult
    {
        public bool IsValid { get; set; }
        public string StatusMessage { get; set; }
        public string SignerFingerprint { get; set; }
        public string SignerUid { get; set; }
        public DateTime? SignDate { get; set; }
        public string VerifiedText { get; set; }

        public SignVerifyResult()
        {
            StatusMessage = "";
            SignerFingerprint = "";
            SignerUid = "";
            VerifiedText = "";
        }
    }

    public class GpgImportSummary
    {
        public bool Success { get; set; }
        public int TotalProcessed { get; set; }
        public int ImportedCount { get; set; }
        public int UnchangedCount { get; set; }
        public int SecretImportedCount { get; set; }
        public List<string> ImportedKeyDetails { get; set; }
        public string RawOutput { get; set; }
        public string ErrorMessage { get; set; }

        public GpgImportSummary()
        {
            ImportedKeyDetails = new List<string>();
            RawOutput = "";
            ErrorMessage = "";
        }
    }
}
