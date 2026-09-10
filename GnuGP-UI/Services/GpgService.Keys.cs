using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace GpgUi
{
    public partial class GpgService
    {
        // ─── 鍵認証署名 ────────────────────────────────────────────────────────

        /// <summary>
        /// 鍵認証署名 (Key Certification) を行う。
        /// --sign-key: 完全署名（エクスポート可）
        /// --lsign-key: ローカル署名（エクスポート不可）
        /// </summary>
        public async Task<CryptoResult> SignKeyAsync(string targetFingerprint, string signerFingerprint, string passphrase, bool localOnly)
        {
            if (string.IsNullOrEmpty(targetFingerprint))
                return new CryptoResult { Success = false, ErrorMessage = "署名対象の鍵フィンガープリントが指定されていません。" };
            if (string.IsNullOrEmpty(signerFingerprint))
                return new CryptoResult { Success = false, ErrorMessage = "署名に使用する秘密鍵を選択してください。" };

            string safeTarget = SanitizeHex(targetFingerprint);
            string safeSigner = SanitizeHex(signerFingerprint);
            if (string.IsNullOrEmpty(safeTarget) || string.IsNullOrEmpty(safeSigner))
                return new CryptoResult { Success = false, ErrorMessage = "フィンガープリントの形式が無効です。" };

            string cmd = localOnly ? "--lsign-key" : "--sign-key";
            string args = string.Format("--batch --yes --local-user {0} {1} {2}", safeSigner, cmd, safeTarget);

            var res = await RunGpgAsync(args, null, string.IsNullOrEmpty(passphrase) ? null : passphrase);
            bool success = res.Item1 == 0;
            return new CryptoResult
            {
                Success = success,
                ErrorMessage = success ? "" : FilterStatusTags(res.Item3)
            };
        }

        // ─── 鍵一覧・パース ────────────────────────────────────────────────────

        public async Task<List<GpgKey>> GetKeysAsync()
        {
            var pubTask = ParseKeysFromColonOutputAsync("--list-keys --with-colons", false);
            var secTask = ParseKeysFromColonOutputAsync("--list-secret-keys --with-colons", true);

            await Task.WhenAll(pubTask, secTask).ConfigureAwait(false);

            var publicKeys = pubTask.Result;
            var secretKeys = secTask.Result;

            return await Task.Run(() =>
            {
                var secretFprs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var sk in secretKeys)
                {
                    if (!string.IsNullOrEmpty(sk.Fingerprint)) secretFprs.Add(sk.Fingerprint);
                }

                foreach (var pk in publicKeys)
                {
                    if (secretFprs.Contains(pk.Fingerprint))
                    {
                        pk.IsSecretKey = true;
                    }
                }

                return publicKeys;
            }).ConfigureAwait(false);
        }

        private static string GetPart(string[] parts, int index)
        {
            return (parts != null && index >= 0 && index < parts.Length) ? parts[index] : "";
        }

        private async Task<List<GpgKey>> ParseKeysFromColonOutputAsync(string args, bool isSecret)
        {
            var res = await RunGpgAsync(args).ConfigureAwait(false);
            if (res.Item1 != 0) return new List<GpgKey>();

            return await Task.Run(() =>
            {
                var keys = new List<GpgKey>();
                GpgKey currentKey = null;
                GpgSubkey currentSubkey = null;

                var lines = res.Item2.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var parts = line.Split(':');
                    if (parts.Length < 2) continue;

                    string recType = parts[0];
                    string algo15 = GetPart(parts, 15);
                    string algo = !string.IsNullOrEmpty(algo15) ? algo15 : GetPart(parts, 3);

                    if (recType == "pub" || recType == "sec")
                    {
                        currentKey = new GpgKey
                        {
                            KeyType = recType,
                            Trust = GetPart(parts, 1),
                            KeyLength = ParseInt(GetPart(parts, 2)),
                            Algorithm = algo,
                            KeyId = GetPart(parts, 4),
                            CreationDate = ParseUnixDate(GetPart(parts, 5)),
                            ExpirationDate = ParseUnixDate(GetPart(parts, 6)),
                            IsSecretKey = isSecret || recType == "sec"
                        };
                        currentSubkey = null;
                        keys.Add(currentKey);
                    }
                    else if ((recType == "sub" || recType == "ssb") && currentKey != null)
                    {
                        currentSubkey = new GpgSubkey
                        {
                            KeyType = recType,
                            Trust = GetPart(parts, 1),
                            KeyLength = ParseInt(GetPart(parts, 2)),
                            Algorithm = algo,
                            KeyId = GetPart(parts, 4),
                            CreationDate = ParseUnixDate(GetPart(parts, 5)),
                            ExpirationDate = ParseUnixDate(GetPart(parts, 6)),
                            Capabilities = GetPart(parts, 11)
                        };
                        currentKey.Subkeys.Add(currentSubkey);
                    }
                    else if (recType == "fpr")
                    {
                        string fpr = GetPart(parts, 9);
                        if (currentSubkey != null)
                        {
                            currentSubkey.Fingerprint = fpr;
                        }
                        else if (currentKey != null && string.IsNullOrEmpty(currentKey.Fingerprint))
                        {
                            currentKey.Fingerprint = fpr;
                        }
                    }
                    else if (recType == "uid" && currentKey != null)
                    {
                        string rawUid = GetPart(parts, 9);
                        var uid = ParseUid(rawUid, GetPart(parts, 1));
                        currentKey.Uids.Add(uid);
                    }
                }

                return keys;
            }).ConfigureAwait(false);
        }

        private GpgUid ParseUid(string rawUid, string trust)
        {
            var uid = new GpgUid { RawUid = rawUid, Trust = trust, Name = rawUid };
            if (string.IsNullOrEmpty(rawUid)) return uid;

            var match = Regex.Match(rawUid, @"^(.*?)(?:\s*\((.*?)\))?(?:\s*<(.*?)>)?$");
            if (match.Success)
            {
                string name = match.Groups[1].Value.Trim();
                string comment = match.Groups[2].Value.Trim();
                string email = match.Groups[3].Value.Trim();

                if (!string.IsNullOrEmpty(name)) uid.Name = name;
                uid.Comment = comment;
                uid.Email = email;
            }
            return uid;
        }

        // ─── 鍵生成・インポート・エクスポート・削除 ────────────────────────────

        public async Task<CryptoResult> GenerateKeyAsync(string name, string email, string comment, string algoChoice, string expireOption, string passphrase)
        {
            string primaryType = "EDDSA";
            string primaryCurve = "ed25519";
            string subType = "ECDH";
            string subCurve = "cv25519";

            if (algoChoice == "Ed448")
            {
                primaryType = "EDDSA";
                primaryCurve = "ed448";
                subType = "ECDH";
                subCurve = "cv448";
            }

            string expireSetting = "0";
            if (expireOption == "1 Year") expireSetting = "1y";
            else if (expireOption == "2 Years") expireSetting = "2y";
            else if (expireOption == "5 Years") expireSetting = "5y";

            string cleanName = SanitizeBatchParam(name);
            string cleanEmail = SanitizeBatchParam(email);
            string cleanComment = SanitizeBatchParam(comment);

            var scriptBuilder = new StringBuilder();
            scriptBuilder.AppendLine("Key-Type: " + primaryType);
            scriptBuilder.AppendLine("Key-Curve: " + primaryCurve);
            scriptBuilder.AppendLine("Key-Usage: sign");
            scriptBuilder.AppendLine("Subkey-Type: " + subType);
            scriptBuilder.AppendLine("Subkey-Curve: " + subCurve);
            scriptBuilder.AppendLine("Subkey-Usage: encrypt");
            scriptBuilder.AppendLine("Name-Real: " + cleanName);
            if (!string.IsNullOrEmpty(cleanEmail)) scriptBuilder.AppendLine("Name-Email: " + cleanEmail);
            if (!string.IsNullOrEmpty(cleanComment)) scriptBuilder.AppendLine("Name-Comment: " + cleanComment);
            scriptBuilder.AppendLine("Expire-Date: " + expireSetting);
            if (!string.IsNullOrEmpty(passphrase))
            {
                scriptBuilder.AppendLine("Passphrase: " + SanitizeBatchParam(passphrase));
            }
            else
            {
                scriptBuilder.AppendLine("%no-protection");
            }
            scriptBuilder.AppendLine("%commit");

            var res = await RunGpgAsync("--batch --gen-key", scriptBuilder.ToString());
            if (res.Item1 == 0 || res.Item3.Contains("key") && res.Item3.Contains("marked as ultimately trusted"))
            {
                return new CryptoResult { Success = true, OutputText = "鍵ペアの生成に成功しました！" };
            }
            else
            {
                return new CryptoResult { Success = false, ErrorMessage = string.IsNullOrEmpty(res.Item3) ? "鍵ペアの生成に失敗しました。" : res.Item3 };
            }
        }

        public async Task<string> ExportPublicKeyAsync(string fingerprint)
        {
            string safeFpr = SanitizeHex(fingerprint);
            var res = await RunGpgAsync("--batch --armor --export -- " + safeFpr);
            return res.Item2;
        }

        public async Task<string> ExportSecretKeyAsync(string fingerprint, string passphrase)
        {
            string safeFpr = SanitizeHex(fingerprint);
            var res = await RunGpgAsync(
                "--batch --armor --export-secret-keys -- " + safeFpr,
                null,
                string.IsNullOrEmpty(passphrase) ? null : passphrase
            );
            return res.Item2;
        }

        public async Task<CryptoResult> ImportKeyAsync(string keyData, Action<string> onDataReceived = null)
        {
            if (string.IsNullOrWhiteSpace(keyData))
            {
                return new CryptoResult { Success = false, ErrorMessage = "インポートする鍵データが空です。" };
            }

            // 大量データまたは標準インポートの安定化のため一時ファイルを作成してファイル経由で処理
            string tempFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "gnupg_import_" + Guid.NewGuid().ToString("N") + ".asc");
            try
            {
                System.IO.File.WriteAllText(tempFile, keyData, new UTF8Encoding(false));
                return await ImportKeyFilesAsync(new[] { tempFile }, onDataReceived);
            }
            finally
            {
                SecureDeleteFile(tempFile);
            }
        }

        public async Task<CryptoResult> ImportKeyFilesAsync(IEnumerable<string> filePaths, Action<string> onDataReceived = null)
        {
            if (filePaths == null || !filePaths.Any())
            {
                return new CryptoResult { Success = false, ErrorMessage = "インポートするファイルが指定されていません。" };
            }

            var validPaths = filePaths.Where(p => !string.IsNullOrEmpty(p) && System.IO.File.Exists(p)).ToList();
            if (validPaths.Count == 0)
            {
                return new CryptoResult { Success = false, ErrorMessage = "有効な鍵ファイルが存在しません。" };
            }

            var sb = new StringBuilder("--batch --import ");
            foreach (var path in validPaths)
            {
                sb.Append(QuotePath(path) + " ");
            }

            var res = await RunGpgAsync(sb.ToString(), null, null, onDataReceived, 600000);
            return BuildCryptoResult(res);
        }

        public async Task<CryptoResult> ReceiveKeysAsync(IEnumerable<string> keyIds, string keyserver = "hkps://keyserver.ubuntu.com", Action<string> onDataReceived = null)
        {
            if (keyIds == null || !keyIds.Any())
            {
                return new CryptoResult { Success = false, ErrorMessage = "取得対象のキーIDまたはフィンガープリントが指定されていません。" };
            }

            var validKeys = keyIds.Select(k => SanitizeHex(k)).Where(k => !string.IsNullOrEmpty(k)).ToList();
            if (validKeys.Count == 0)
            {
                return new CryptoResult { Success = false, ErrorMessage = "有効なキーIDまたはフィンガープリントが存在しません。" };
            }

            if (string.IsNullOrEmpty(keyserver))
            {
                keyserver = "hkps://keyserver.ubuntu.com";
            }

            var sb = new StringBuilder();
            sb.Append(string.Format("--batch --keyserver {0} --recv-keys ", keyserver));
            foreach (var id in validKeys)
            {
                sb.Append(id + " ");
            }

            var res = await RunGpgAsync(sb.ToString(), null, null, onDataReceived, 600000);
            return BuildCryptoResult(res);
        }

        private static CryptoResult BuildCryptoResult(Tuple<int, string, string> res)
        {
            bool isTimeout = res.Item1 == -1;
            bool success = !isTimeout && (res.Item1 == 0 || res.Item3.Contains("imported") || res.Item3.Contains("not changed") || res.Item3.Contains("IMPORT_OK") || res.Item3.Contains("インポート") || res.Item3.Contains("変更なし"));
            return new CryptoResult
            {
                Success = success,
                OutputText = res.Item3,
                ErrorMessage = success ? "" : (isTimeout ? res.Item3 : FilterStatusTags(res.Item3))
            };
        }

        public static GpgImportSummary ParseImportSummary(string rawOutput, bool processSuccess)
        {
            var summary = new GpgImportSummary
            {
                Success = processSuccess,
                RawOutput = rawOutput
            };

            if (string.IsNullOrEmpty(rawOutput)) return summary;

            var lines = rawOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                if (line.Contains("[GNUPG:] IMPORT_RES"))
                {
                    var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 7)
                    {
                        int t, imp, unc;
                        if (int.TryParse(parts[2], out t)) summary.TotalProcessed = Math.Max(summary.TotalProcessed, t);
                        if (int.TryParse(parts[4], out imp)) summary.ImportedCount = Math.Max(summary.ImportedCount, imp);
                        if (int.TryParse(parts[6], out unc)) summary.UnchangedCount = Math.Max(summary.UnchangedCount, unc);
                    }
                }

                var matchImportOk = Regex.Match(line, @"\[GNUPG:\]\s+IMPORT_OK\s+(\d+)", RegexOptions.IgnoreCase);
                if (matchImportOk.Success)
                {
                    summary.TotalProcessed++;
                    int reasonFlag = 0;
                    int.TryParse(matchImportOk.Groups[1].Value, out reasonFlag);
                    if (reasonFlag == 0) summary.UnchangedCount++;
                    else summary.ImportedCount++;
                }

                var matchTotal = Regex.Match(line, @"(?:Total number processed|処理数の合計)\s*:\s*(\d+)", RegexOptions.IgnoreCase);
                if (matchTotal.Success)
                {
                    int val;
                    if (int.TryParse(matchTotal.Groups[1].Value, out val)) summary.TotalProcessed = Math.Max(summary.TotalProcessed, val);
                }

                var matchImported = Regex.Match(line, @"(?:imported|インポート)\s*:\s*(\d+)", RegexOptions.IgnoreCase);
                if (matchImported.Success && !line.Contains("secret") && !line.Contains("秘密"))
                {
                    int val;
                    if (int.TryParse(matchImported.Groups[1].Value, out val)) summary.ImportedCount = Math.Max(summary.ImportedCount, val);
                }

                var matchUnchanged = Regex.Match(line, @"(?:unchanged|変更なし)\s*:\s*(\d+)", RegexOptions.IgnoreCase);
                if (matchUnchanged.Success)
                {
                    int val;
                    if (int.TryParse(matchUnchanged.Groups[1].Value, out val)) summary.UnchangedCount = Math.Max(summary.UnchangedCount, val);
                }

                var matchSecret = Regex.Match(line, @"(?:secret keys imported|秘密鍵のインポート)\s*:\s*(\d+)", RegexOptions.IgnoreCase);
                if (matchSecret.Success)
                {
                    int val;
                    if (int.TryParse(matchSecret.Groups[1].Value, out val)) summary.SecretImportedCount = Math.Max(summary.SecretImportedCount, val);
                }

                var matchDetail = Regex.Match(line, @"(?:key|鍵)\s+([0-9a-fA-F]+):\s*(?:public key\s+|公開鍵\s+)?(.+?)\s+(imported|not changed|new subkeys|インポートされました|変更なし)", RegexOptions.IgnoreCase);
                if (matchDetail.Success)
                {
                    string keyId = matchDetail.Groups[1].Value;
                    string uid = matchDetail.Groups[2].Value.Trim('"');
                    string status = matchDetail.Groups[3].Value;
                    string statusJp = (status.Contains("imported") || status.Contains("インポート")) ? "新規取り込み" : ((status.Contains("not changed") || status.Contains("変更なし")) ? "変更なし" : "サブキー更新");
                    summary.ImportedKeyDetails.Add(string.Format("・ {0} [ID: {1}] ({2})", uid, keyId, statusJp));
                }
            }

            if (summary.TotalProcessed == 0 && (summary.ImportedCount > 0 || summary.ImportedKeyDetails.Count > 0))
            {
                summary.TotalProcessed = Math.Max(summary.ImportedCount, summary.ImportedKeyDetails.Count);
            }

            return summary;
        }

        public async Task<CryptoResult> DeleteKeyAsync(string fingerprint, bool isSecret)
        {
            string safeFpr = SanitizeHex(fingerprint);
            if (isSecret)
            {
                await RunGpgAsync("--batch --yes --delete-secret-keys -- " + safeFpr);
            }
            var res = await RunGpgAsync("--batch --yes --delete-keys -- " + safeFpr);
            return new CryptoResult { Success = res.Item1 == 0, ErrorMessage = res.Item3 };
        }
    }
}
