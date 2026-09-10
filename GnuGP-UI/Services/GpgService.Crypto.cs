using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace GpgUi
{
    public partial class GpgService
    {
        // ─── 暗号化共通引数ビルダー ────────────────────────────────────────────

        /// <summary>
        /// 受信者・対称鍵・署名者の指定から GPG 暗号化用のコア引数と stdin パスフレーズを構築する。
        /// --batch / --armor / --output は呼び出し元で付与すること。
        /// 戻り値が null の場合は受信者もパスフレーズも未指定（呼び出し元でエラー扱い）。
        /// </summary>
        private static string BuildEncryptArgs(
            List<string> recipientFingerprints,
            string passphrase,
            string signFingerprint,
            string signPassphrase,
            out string stdinPassphrase)
        {
            stdinPassphrase = null;
            var sb = new StringBuilder();

            if (recipientFingerprints != null && recipientFingerprints.Count > 0)
            {
                // 公開鍵暗号化: フィンガープリントを 16 進サニタイズして --recipient に付与
                foreach (var r in recipientFingerprints)
                {
                    string safeR = Regex.Replace(r ?? "", @"[^0-9a-fA-F]", "");
                    if (!string.IsNullOrEmpty(safeR))
                        sb.Append("--recipient " + safeR + " ");
                }
                sb.Append("--encrypt ");
            }
            else if (!string.IsNullOrEmpty(passphrase))
            {
                // 対称鍵暗号化: パスフレーズを stdin 経由で渡す
                sb.Append("--symmetric ");
                stdinPassphrase = passphrase;
            }
            else
            {
                return null; // 受信者もパスフレーズも未指定
            }

            if (!string.IsNullOrEmpty(signFingerprint))
            {
                // 署名を同時に行う場合: 署名者を --local-user で指定
                string safeSignFpr = Regex.Replace(signFingerprint, @"[^0-9a-fA-F]", "");
                if (!string.IsNullOrEmpty(safeSignFpr))
                {
                    sb.Append("--local-user " + safeSignFpr + " --sign ");
                    // 対称鍵暗号化でないときのみ署名パスフレーズを stdin に設定
                    if (!string.IsNullOrEmpty(signPassphrase) && stdinPassphrase == null)
                        stdinPassphrase = signPassphrase;
                }
            }

            return sb.ToString().TrimEnd();
        }

        // ─── テキスト暗号化・復号 ─────────────────────────────────────────────

        public async Task<CryptoResult> EncryptTextAsync(string text, List<string> recipientFingerprints, string passphrase, string signFingerprint, string signPassphrase)
        {
            if ((recipientFingerprints == null || recipientFingerprints.Count == 0) &&
                !string.IsNullOrEmpty(passphrase) &&
                !string.IsNullOrEmpty(signFingerprint))
            {
                var signed = await SignTextAsync(text, signFingerprint, "Ascii", signPassphrase);
                if (!signed.Success)
                {
                    return signed;
                }

                return await EncryptTextAsync(signed.OutputText, recipientFingerprints, passphrase, null, null);
            }

            string stdinPassphrase;
            string encryptArgs = BuildEncryptArgs(recipientFingerprints, passphrase, signFingerprint, signPassphrase, out stdinPassphrase);
            if (encryptArgs == null)
            {
                return new CryptoResult { Success = false, ErrorMessage = "受信者の公開鍵を選択するか、共通鍵パスフレーズを入力してください。" };
            }

            // テキスト暗号化は ASCII Armor 固定
            string fullArgs = string.Format("--batch --armor {0}", encryptArgs);
            var res = await RunGpgAsync(fullArgs, text, stdinPassphrase);
            return new CryptoResult
            {
                Success = res.Item1 == 0 && !string.IsNullOrEmpty(res.Item2),
                OutputText = res.Item2,
                ErrorMessage = res.Item3
            };
        }

        public async Task<CryptoResult> DecryptTextAsync(string cipherText, string passphrase)
        {
            var res = await RunGpgAsync(
                "--batch --decrypt",
                cipherText,
                string.IsNullOrEmpty(passphrase) ? null : passphrase
            );

            if (res.Item1 == 0 && LooksLikeArmoredPgpMessage(res.Item2))
            {
                var nested = await VerifyTextAsync(res.Item2, null);
                if (!string.IsNullOrEmpty(nested.VerifiedText) || nested.IsValid)
                {
                    return new CryptoResult
                    {
                        Success = nested.IsValid,
                        OutputText = nested.VerifiedText,
                        SignerInfo = nested.SignerUid,
                        ErrorMessage = nested.IsValid ? "" : nested.StatusMessage
                    };
                }
            }

            return new CryptoResult
            {
                Success = res.Item1 == 0,
                OutputText = res.Item2,
                SignerInfo = FilterStatusTags(res.Item3),
                ErrorMessage = res.Item1 == 0 ? "" : FilterStatusTags(res.Item3)
            };
        }

        // ─── ファイル暗号化・復号 ─────────────────────────────────────────────

        public async Task<CryptoResult> EncryptFileAsync(string inputFilePath, string outputFilePath, List<string> recipientFingerprints, string passphrase, string signFingerprint, string signPassphrase)
        {
            if (string.IsNullOrEmpty(inputFilePath) || !File.Exists(inputFilePath))
            {
                return new CryptoResult { Success = false, ErrorMessage = "指定された入力ファイルが存在しません。" };
            }

            if ((recipientFingerprints == null || recipientFingerprints.Count == 0) &&
                !string.IsNullOrEmpty(passphrase) &&
                !string.IsNullOrEmpty(signFingerprint))
            {
                string tempSignedPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".signed.asc");
                try
                {
                    var signed = await SignFileAsync(inputFilePath, tempSignedPath, signFingerprint, "Ascii", signPassphrase);
                    if (!signed.Success)
                    {
                        return signed;
                    }

                    return await EncryptFileAsync(tempSignedPath, outputFilePath, recipientFingerprints, passphrase, null, null);
                }
                finally
                {
                    if (File.Exists(tempSignedPath))
                    {
                        SecureDeleteFile(tempSignedPath);
                    }
                }
            }

            string stdinPassphrase;
            string encryptArgs = BuildEncryptArgs(recipientFingerprints, passphrase, signFingerprint, signPassphrase, out stdinPassphrase);
            if (encryptArgs == null)
            {
                return new CryptoResult { Success = false, ErrorMessage = "受信者の公開鍵を選択するか、共通鍵パスフレーズを入力してください。" };
            }

            // 出力形式: 拡張子が .asc なら ASCII Armor、それ以外はバイナリ
            bool useArmor = !string.IsNullOrEmpty(outputFilePath) &&
                            outputFilePath.EndsWith(".asc", StringComparison.OrdinalIgnoreCase);

            string fullArgs = string.Format(
                "--batch --yes {0}--output {1} {2} -- {3}",
                useArmor ? "--armor " : "",
                QuotePath(outputFilePath),
                encryptArgs,
                QuotePath(inputFilePath));

            var res = await RunGpgAsync(fullArgs, null, stdinPassphrase);
            return new CryptoResult
            {
                Success = res.Item1 == 0 && File.Exists(outputFilePath),
                ErrorMessage = res.Item1 == 0 ? "" : FilterStatusTags(res.Item3)
            };
        }

        public async Task<CryptoResult> DecryptFileAsync(string inputFilePath, string passphrase)
        {
            if (string.IsNullOrEmpty(inputFilePath) || !File.Exists(inputFilePath))
            {
                return new CryptoResult { Success = false, ErrorMessage = "指定されたファイルが存在しません。" };
            }

            var res = await RunGpgAsync(
                string.Format("--batch --decrypt -- {0}", QuotePath(inputFilePath)),
                null,
                string.IsNullOrEmpty(passphrase) ? null : passphrase
            );

            if (res.Item1 == 0 && LooksLikeArmoredPgpMessage(res.Item2))
            {
                var nested = await VerifyTextAsync(res.Item2, null);
                if (!string.IsNullOrEmpty(nested.VerifiedText) || nested.IsValid)
                {
                    return new CryptoResult
                    {
                        Success = nested.IsValid,
                        OutputText = nested.VerifiedText,
                        SignerInfo = nested.SignerUid,
                        ErrorMessage = nested.IsValid ? "" : nested.StatusMessage
                    };
                }
            }

            return new CryptoResult
            {
                Success = res.Item1 == 0,
                OutputText = res.Item2,
                SignerInfo = FilterStatusTags(res.Item3),
                ErrorMessage = res.Item1 == 0 ? "" : FilterStatusTags(res.Item3)
            };
        }

        public async Task<CryptoResult> DecryptFileToPathAsync(string inputFilePath, string outputFilePath, string passphrase)
        {
            if (string.IsNullOrEmpty(inputFilePath) || !File.Exists(inputFilePath))
            {
                return new CryptoResult { Success = false, ErrorMessage = "指定された入力ファイルが存在しません。" };
            }

            var res = await RunGpgAsync(
                string.Format("--batch --yes --output {0} --decrypt -- {1}", QuotePath(outputFilePath), QuotePath(inputFilePath)),
                null,
                string.IsNullOrEmpty(passphrase) ? null : passphrase
            );

            if (res.Item1 == 0 && LooksLikeArmoredPgpFile(outputFilePath))
            {
                string tempOutputPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + Path.GetExtension(outputFilePath));
                try
                {
                    var nested = await DecryptFileToPathAsync(outputFilePath, tempOutputPath, null);
                    if (!nested.Success)
                    {
                        return nested;
                    }

                    if (File.Exists(outputFilePath))
                    {
                        SecureDeleteFile(outputFilePath);
                    }

                    File.Move(tempOutputPath, outputFilePath);
                    return new CryptoResult
                    {
                        Success = true,
                        SignerInfo = nested.SignerInfo,
                        ErrorMessage = ""
                    };
                }
                finally
                {
                    if (File.Exists(tempOutputPath))
                    {
                        SecureDeleteFile(tempOutputPath);
                    }
                }
            }

            return new CryptoResult
            {
                Success = res.Item1 == 0 && File.Exists(outputFilePath),
                SignerInfo = FilterStatusTags(res.Item3),
                ErrorMessage = res.Item1 == 0 ? "" : FilterStatusTags(res.Item3)
            };
        }

        // ─── 署名・検証 ────────────────────────────────────────────────────────

        public async Task<CryptoResult> SignTextAsync(string text, string signFingerprint, string mode, string passphrase)
        {
            string modeArg = "--clearsign";
            if (mode == "Detached") modeArg = "--detach-sign --armor";
            else if (mode == "Ascii") modeArg = "--sign --armor";

            string safeSignFpr = SanitizeHex(signFingerprint);

            var res = await RunGpgAsync(
                string.Format("--batch --local-user {0} {1}", safeSignFpr, modeArg),
                text,
                string.IsNullOrEmpty(passphrase) ? null : passphrase
            );
            return new CryptoResult
            {
                Success = res.Item1 == 0 && !string.IsNullOrEmpty(res.Item2),
                OutputText = res.Item2,
                ErrorMessage = res.Item3
            };
        }

        public async Task<CryptoResult> SignFileAsync(string inputFilePath, string outputFilePath, string signFingerprint, string mode, string passphrase)
        {
            if (string.IsNullOrEmpty(inputFilePath) || !File.Exists(inputFilePath))
            {
                return new CryptoResult { Success = false, ErrorMessage = "指定された入力ファイルが存在しません。" };
            }

            string modeArg = "--clearsign";
            if (mode == "Detached") modeArg = "--detach-sign --armor";
            else if (mode == "Ascii") modeArg = "--sign --armor";

            string safeSignFpr = SanitizeHex(signFingerprint);

            var res = await RunGpgAsync(
                string.Format("--batch --yes --local-user {0} --output {1} {2} -- {3}", safeSignFpr, QuotePath(outputFilePath), modeArg, QuotePath(inputFilePath)),
                null,
                string.IsNullOrEmpty(passphrase) ? null : passphrase
            );

            return new CryptoResult
            {
                Success = res.Item1 == 0 && File.Exists(outputFilePath),
                ErrorMessage = res.Item1 == 0 ? "" : FilterStatusTags(res.Item3)
            };
        }

        public async Task<SignVerifyResult> VerifyFileAsync(string targetFilePath, string detachedSigPath)
        {
            if (string.IsNullOrEmpty(targetFilePath) || !File.Exists(targetFilePath))
            {
                return new SignVerifyResult { IsValid = false, StatusMessage = "検証対象ファイルが存在しません。" };
            }

            Tuple<int, string, string> res;

            if (!string.IsNullOrEmpty(detachedSigPath) && File.Exists(detachedSigPath))
            {
                res = await RunGpgAsync(string.Format("--batch --verify -- {0} {1}", QuotePath(detachedSigPath), QuotePath(targetFilePath)));
            }
            else
            {
                res = await RunGpgAsync(string.Format("--batch --verify -- {0}", QuotePath(targetFilePath)));
            }

            return ParseVerifyResult(res.Item3, res.Item2, res.Item1);
        }

        public async Task<SignVerifyResult> VerifyTextAsync(string text, string signatureText)
        {
            Tuple<int, string, string> res;
            if (!string.IsNullOrEmpty(signatureText))
            {
                string tempDir = Path.GetTempPath();
                string randomName = Guid.NewGuid().ToString("N");
                string tempFile = Path.Combine(tempDir, randomName + ".tmp");
                string sigFile = Path.Combine(tempDir, randomName + ".sig");
                try
                {
                    File.WriteAllText(tempFile, text, Encoding.UTF8);
                    File.WriteAllText(sigFile, signatureText, Encoding.UTF8);
                    res = await RunGpgAsync(string.Format("--batch --verify -- {0} {1}", QuotePath(sigFile), QuotePath(tempFile)));
                }
                finally
                {
                    SecureDeleteFile(tempFile);
                    SecureDeleteFile(sigFile);
                }
            }
            else
            {
                res = await RunGpgAsync("--batch --decrypt --", text);
            }

            return ParseVerifyResult(res.Item3, res.Item2, res.Item1);
        }

        private static SignVerifyResult ParseVerifyResult(string statusOutput, string stdout, int exitCode)
        {
            bool hasGoodSig = statusOutput.Contains("[GNUPG:] GOODSIG") || statusOutput.Contains("[GNUPG:] VALIDSIG");
            bool hasBadSig = statusOutput.Contains("[GNUPG:] BADSIG") || statusOutput.Contains("[GNUPG:] ERRSIG") || statusOutput.Contains("[GNUPG:] EXPKEYSIG") || statusOutput.Contains("[GNUPG:] REVKEYSIG");

            bool isValid = exitCode == 0 && hasGoodSig && !hasBadSig;

            var result = new SignVerifyResult
            {
                IsValid = isValid,
                StatusMessage = FilterStatusTags(statusOutput),
                VerifiedText = stdout
            };

            var matchValidSig = Regex.Match(statusOutput, @"\[GNUPG:\]\s+VALIDSIG\s+([0-9A-Fa-f]+)");
            if (matchValidSig.Success)
            {
                result.SignerFingerprint = matchValidSig.Groups[1].Value;
            }

            var matchGoodSigUid = Regex.Match(statusOutput, @"\[GNUPG:\]\s+GOODSIG\s+[0-9A-Fa-f]+\s+(.+)");
            if (matchGoodSigUid.Success)
            {
                result.SignerUid = matchGoodSigUid.Groups[1].Value.Trim();
            }

            return result;
        }

        private static bool LooksLikeArmoredPgpMessage(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            string trimmed = text.TrimStart();
            return trimmed.StartsWith("-----BEGIN PGP MESSAGE-----", StringComparison.Ordinal) ||
                   trimmed.StartsWith("-----BEGIN PGP SIGNED MESSAGE-----", StringComparison.Ordinal) ||
                   trimmed.StartsWith("-----BEGIN PGP SIGNATURE-----", StringComparison.Ordinal);
        }

        private static bool LooksLikeArmoredPgpFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return false;

            try
            {
                using (var reader = new StreamReader(filePath, Encoding.UTF8, true))
                {
                    for (int i = 0; i < 5; i++)
                    {
                        string line = reader.ReadLine();
                        if (string.IsNullOrEmpty(line)) continue;
                        string trimmed = line.Trim();
                        if (trimmed.StartsWith("-----BEGIN PGP MESSAGE-----", StringComparison.Ordinal) ||
                            trimmed.StartsWith("-----BEGIN PGP SIGNED MESSAGE-----", StringComparison.Ordinal) ||
                            trimmed.StartsWith("-----BEGIN PGP SIGNATURE-----", StringComparison.Ordinal))
                        {
                            return true;
                        }
                    }
                }
            }
            catch { }

            return false;
        }
    }
}
