using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace GpgUi
{
    public partial class GpgService
    {
        public string GpgPath { get; set; }

        public GpgService()
        {
            GpgPath = LoadConfigPath();
        }

        public GpgService(string gpgPath)
        {
            GpgPath = string.IsNullOrEmpty(gpgPath) ? "gpg" : gpgPath;
        }

        // ─── 設定ファイル管理 ──────────────────────────────────────────────────

        /// <summary>
        /// アプリ設定ファイル (AppData または実行ディレクトリ) のフルパスを取得
        /// </summary>
        private static string GetConfigFilePath()
        {
            try
            {
                string appDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GpgUi");
                if (!Directory.Exists(appDataDir))
                {
                    Directory.CreateDirectory(appDataDir);
                }
                return Path.Combine(appDataDir, "gpg_path.txt");
            }
            catch
            {
                return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "gpg_path.txt");
            }
        }

        /// <summary>
        /// GnuPG 実行ファイルのパスを自動解決（絶対パスの自動判定、PATH 環境変数検索、またはデフォルト）
        /// </summary>
        public static string ResolveGpgPath(string requestedPath)
        {
            if (!string.IsNullOrEmpty(requestedPath) && requestedPath != "gpg")
            {
                if (File.Exists(requestedPath)) return requestedPath;
            }

            string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

            string[] candidatePaths = new[]
            {
                Path.Combine(pf, "GnuPG", "bin", "gpg.exe"),
                Path.Combine(pf86, "GnuPG", "bin", "gpg.exe"),
                Path.Combine(pf, "GnuPG", "gpg.exe"),
                Path.Combine(pf86, "GnuPG", "gpg.exe")
            };

            foreach (var cp in candidatePaths)
            {
                if (!string.IsNullOrEmpty(cp) && File.Exists(cp)) return cp;
            }

            string pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(pathEnv))
            {
                foreach (var dir in pathEnv.Split(Path.PathSeparator))
                {
                    try
                    {
                        string trimmed = dir.Trim();
                        if (string.IsNullOrEmpty(trimmed)) continue;
                        string fullPath = Path.Combine(trimmed, "gpg.exe");
                        if (File.Exists(fullPath)) return fullPath;
                    }
                    catch { }
                }
            }

            return "gpg";
        }

        public static string LoadConfigPath()
        {
            try
            {
                string configFile = GetConfigFilePath();
                if (File.Exists(configFile))
                {
                    string path = File.ReadAllText(configFile, Encoding.UTF8).Trim();
                    if (!string.IsNullOrEmpty(path)) return ResolveGpgPath(path);
                }

                string legacyFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "gpg_path.txt");
                if (File.Exists(legacyFile))
                {
                    string path = File.ReadAllText(legacyFile, Encoding.UTF8).Trim();
                    if (!string.IsNullOrEmpty(path)) return ResolveGpgPath(path);
                }
            }
            catch { }
            return ResolveGpgPath("gpg");
        }

        public static void SaveConfigPath(string path)
        {
            try
            {
                string text = string.IsNullOrEmpty(path) ? "gpg" : path.Trim();
                string configFile = GetConfigFilePath();
                File.WriteAllText(configFile, text, Encoding.UTF8);
            }
            catch { }
        }

        // ─── サニタイズ ────────────────────────────────────────────────────────

        /// <summary>
        /// 英数字（16進数）以外の文字を除去し、コマンドインジェクションを防止
        /// </summary>
        private static string SanitizeHex(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";
            return Regex.Replace(input, @"[^0-9a-fA-F]", "");
        }

        // ─── プロセス実行コア ──────────────────────────────────────────────────

        /// <summary>
        /// GPG プロセスを実行する。
        /// - パスフレーズは stdin 経由で安全に渡す (--passphrase-fd 0)。
        /// - 機械判別用に --status-fd 2 を自動付与し、ステータス偽装を防止する。
        /// </summary>
        private async Task<Tuple<int, string, string>> RunGpgAsync(string arguments, string inputData = null, string passphrase = null, Action<string> onDataReceived = null, int timeoutMs = 300000)
        {
            return await Task.Run(() =>
            {
                string exe = ResolveGpgPath(GpgPath);

                string finalArgs = "--batch --no-tty --status-fd 2 " + arguments;
                bool hasPassphrase = !string.IsNullOrEmpty(passphrase);
                if (hasPassphrase)
                {
                    finalArgs = "--pinentry-mode loopback --passphrase-fd 0 " + finalArgs;
                }

                bool needsStdin = hasPassphrase || inputData != null;

                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = finalArgs,
                    UseShellExecute = false,
                    RedirectStandardInput = needsStdin,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                using (var process = new Process { StartInfo = psi })
                {
                    var stdOutBuilder = new StringBuilder();
                    var stdErrBuilder = new StringBuilder();

                    process.OutputDataReceived += (s, e) =>
                    {
                        if (e.Data != null)
                        {
                            lock (stdOutBuilder) { stdOutBuilder.AppendLine(e.Data); }
                            if (onDataReceived != null) { onDataReceived(e.Data); }
                        }
                    };

                    process.ErrorDataReceived += (s, e) =>
                    {
                        if (e.Data != null)
                        {
                            lock (stdErrBuilder) { stdErrBuilder.AppendLine(e.Data); }
                            if (onDataReceived != null) { onDataReceived(e.Data); }
                        }
                    };

                    try
                    {
                        if (!process.Start())
                        {
                            return Tuple.Create(-1, "", "エラー: プロセスの起動に失敗しました。");
                        }
                    }
                    catch (Exception ex)
                    {
                        return Tuple.Create(-1, "", "エラー: GPG プロセスの起動に失敗しました。" + ex.Message);
                    }

                    // パイプ詰まりによるデッドロックを防止するため、stdout/stderr の非同期読み込みを即座に開始
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    // stdin へのデータ供給は別タスクで並行して行い、書き込み完了後に Close する
                    Task stdinTask = Task.CompletedTask;
                    if (needsStdin)
                    {
                        stdinTask = Task.Run(() =>
                        {
                            try
                            {
                                using (var writer = new StreamWriter(process.StandardInput.BaseStream, new UTF8Encoding(false)))
                                {
                                    if (hasPassphrase)
                                    {
                                        writer.WriteLine(passphrase);
                                    }
                                    if (inputData != null)
                                    {
                                        writer.Write(inputData);
                                    }
                                    writer.Flush();
                                }
                            }
                            catch { }
                        });
                    }

                    var waitForExitTask = Task.Run(() => process.WaitForExit());
                    var timeoutTask = Task.Delay(timeoutMs);

                    var completedTask = Task.WhenAny(waitForExitTask, timeoutTask).GetAwaiter().GetResult();

                    if (completedTask == timeoutTask)
                    {
                        try { process.Kill(); } catch { }
                        return Tuple.Create(-1, "", string.Format("エラー: GPG プロセスの実行がタイムアウトしました ({0}秒)。", timeoutMs / 1000));
                    }

                    string stdout;
                    string stderr;
                    lock (stdOutBuilder) { stdout = stdOutBuilder.ToString(); }
                    lock (stdErrBuilder) { stderr = stdErrBuilder.ToString(); }

                    return Tuple.Create(process.ExitCode, stdout, stderr);
                }
            }).ConfigureAwait(false);
        }

        // ─── システム情報 ──────────────────────────────────────────────────────

        public async Task<GpgSystemInfo> GetSystemInfoAsync()
        {
            var info = new GpgSystemInfo();
            try
            {
                var res = await RunGpgAsync("--version");
                if (res.Item1 == 0)
                {
                    info.IsAvailable = true;
                    var lines = res.Item2.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    string verStr = lines.Length > 0 ? lines[0] : "GnuPG";
                    info.Version = verStr;

                    var homeMatch = Regex.Match(res.Item2, @"Home:\s*(.+)");
                    if (homeMatch.Success) info.HomeDirectory = homeMatch.Groups[1].Value.Trim();

                    info.SupportedAlgorithms = "Pubkey: Ed25519, Ed448, Cv25519, Cv448 | Cipher: AES256, CAMELLIA256, TWOFISH";
                }
            }
            catch (Exception ex)
            {
                info.IsAvailable = false;
                info.Version = "GnuPG エラー: " + ex.Message;
            }
            return info;
        }

        // ─── ユーティリティ ────────────────────────────────────────────────────

        /// <summary>
        /// コマンドライン引数用ファイルパスのダブルクォート囲みとエスケープ
        /// </summary>
        private static string QuotePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return "\"\"";
            string clean = path.Replace("\"", "");
            return string.Format("\"{0}\"", clean);
        }

        /// <summary>
        /// GnuPG バッチスクリプトのパラメータをサニタイズ。
        /// 改行コードと % プレフィックス（GnuPG バッチコマンド）を除去して
        /// スクリプトインジェクションを防止する。
        /// </summary>
        private static string SanitizeBatchParam(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";
            string cleaned = input.Replace("\r", "").Replace("\n", "");
            cleaned = cleaned.TrimStart('%');
            cleaned = cleaned.Replace("%", "");
            return cleaned;
        }

        /// <summary>
        /// stderr 出力から [GNUPG:] ステータスタグ行を除外し、
        /// ユーザー表示用のメッセージのみを返す。
        /// ステータスタグは機械判定専用であり、UI に露出させてはならない。
        /// </summary>
        public static string FilterStatusTags(string stderrOutput)
        {
            if (string.IsNullOrEmpty(stderrOutput)) return "";
            var sb = new StringBuilder();
            foreach (var line in stderrOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!line.TrimStart().StartsWith("[GNUPG:]"))
                {
                    sb.AppendLine(line);
                }
            }
            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// 一時ファイル上のプレーンテキストをゼロ上書きしてから削除
        /// </summary>
        public static void SecureDeleteFile(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    long length = new FileInfo(filePath).Length;
                    if (length > 0)
                    {
                        byte[] zeros = new byte[length];
                        File.WriteAllBytes(filePath, zeros);
                    }
                    File.Delete(filePath);
                }
            }
            catch { }
        }

        private static int ParseInt(string val)
        {
            int r;
            int.TryParse(val, out r);
            return r;
        }

        private static DateTime? ParseUnixDate(string val)
        {
            long sec;
            if (long.TryParse(val, out sec) && sec > 0)
            {
                return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(sec).ToLocalTime();
            }
            DateTime dt;
            if (DateTime.TryParse(val, out dt))
            {
                return dt;
            }
            return null;
        }
    }
}
