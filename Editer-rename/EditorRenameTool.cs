using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace EditorRenameTool
{
    public class AppConfig
    {
        public string EditorPath { get; set; }
        public string EditorArguments { get; set; }

        public AppConfig()
        {
            EditorPath = string.Empty;
            EditorArguments = "\"{file}\"";
        }
    }

    public class RenameItem
    {
        public string Id { get; set; }
        public string SourcePath { get; set; }
        public string DestinationPath { get; set; }
        public string TemporaryPath { get; set; }
        public bool IsStaged { get; set; }
        public string ErrorMessage { get; set; }

        public RenameItem()
        {
            Id = string.Empty;
            SourcePath = string.Empty;
            DestinationPath = string.Empty;
            TemporaryPath = null;
            IsStaged = false;
            ErrorMessage = string.Empty;
        }
    }

    public class BatchResult
    {
        public List<RenameItem> Success { get; set; }
        public List<RenameItem> Failed { get; set; }

        public BatchResult()
        {
            Success = new List<RenameItem>();
            Failed = new List<RenameItem>();
        }
    }

    public class RetryResult
    {
        public List<RenameItem> RemainingFailedItems { get; set; }

        public RetryResult()
        {
            RemainingFailedItems = new List<RenameItem>();
        }
    }

    public class HistoryEntry
    {
        public string Id { get; set; }
        public string SourcePath { get; set; }
        public string DestinationPath { get; set; }
        // Fallback properties for backwards compatibility with legacy JSON
        public string Original { get; set; }
        public string Current { get; set; }

        public HistoryEntry()
        {
            Id = string.Empty;
            SourcePath = string.Empty;
            DestinationPath = string.Empty;
        }
    }

    public class HistorySession
    {
        public int SchemaVersion { get; set; }
        public string SessionId { get; set; }
        public string Created { get; set; }
        public string Mode { get; set; }
        public string RelatedSession { get; set; }
        public bool Undone { get; set; }
        public string UndoneAt { get; set; }
        public List<HistoryEntry> Entries { get; set; }

        [ScriptIgnore]
        public string FilePath { get; set; }

        public HistorySession()
        {
            SchemaVersion = 1;
            SessionId = string.Empty;
            Created = string.Empty;
            Mode = "Rename";
            RelatedSession = string.Empty;
            Undone = false;
            UndoneAt = null;
            Entries = new List<HistoryEntry>();
            FilePath = string.Empty;
        }
    }

    public class ParseResult
    {
        public List<RenameItem> Items { get; set; }
        public List<string> Errors { get; set; }

        public ParseResult()
        {
            Items = new List<RenameItem>();
            Errors = new List<string>();
        }
    }

    public static class Program
    {
        public const string AppName = "Editor Rename Tool";

        public static string AppDirectory
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "EditorRenameTool"
                );
            }
        }

        public static string WorkDirectory
        {
            get { return Path.Combine(AppDirectory, "Work"); }
        }

        public static string HistoryDirectory
        {
            get { return Path.Combine(AppDirectory, "History"); }
        }

        public static string ConfigPath
        {
            get { return Path.Combine(AppDirectory, "config.json"); }
        }

        public static string LogPath
        {
            get { return Path.Combine(AppDirectory, "application.log"); }
        }

        [STAThread]
        public static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            try
            {
                if (!EnsureDirectories())
                {
                    return;
                }

                RunApplication(args);
            }
            catch (Exception ex)
            {
                WriteAppLog(ex.ToString());

                MessageBox.Show(
                    "予期しないエラーが発生しました。\r\n\r\n" +
                    ex.Message +
                    "\r\n\r\nログ:\r\n" +
                    LogPath,
                    AppName,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            }
        }

        private static void RunApplication(string[] args)
        {
            if (args != null && args.Length > 0)
            {
                bool silent = false;
                List<string> fileArgs = new List<string>();
                for (int i = 0; i < args.Length; i++)
                {
                    string argument = args[i].Trim();
                    if (string.Equals(argument, "--silent", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(argument, "-s", StringComparison.OrdinalIgnoreCase))
                    {
                        silent = true;
                    }
                    else
                    {
                        fileArgs.Add(argument);
                    }
                }

                if (fileArgs.Count > 0)
                {
                    string first = fileArgs[0];
                    if (string.Equals(first, "--install", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(first, "/install", StringComparison.OrdinalIgnoreCase))
                    {
                        InstallShortcut(!silent);
                        return;
                    }

                    if (string.Equals(first, "--uninstall", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(first, "/uninstall", StringComparison.OrdinalIgnoreCase))
                    {
                        UninstallShortcut(!silent);
                        return;
                    }

                    if (string.Equals(first, "--settings", StringComparison.OrdinalIgnoreCase))
                    {
                        ShowEditorSettings(null);
                        return;
                    }

                    if (string.Equals(first, "--history", StringComparison.OrdinalIgnoreCase))
                    {
                        StartUndoOperation(null);
                        return;
                    }

                    // File list passed from "Send To" or command line
                    StartRenameOperation(fileArgs.ToArray());
                }
                else
                {
                    Application.Run(new MainForm());
                }
            }
            else
            {
                // No arguments: show main menu window
                Application.Run(new MainForm());
            }
        }

        public static bool EnsureDirectories()
        {
            try
            {
                Directory.CreateDirectory(AppDirectory);
                Directory.CreateDirectory(WorkDirectory);
                Directory.CreateDirectory(HistoryDirectory);
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "作業フォルダーを作成できませんでした。\r\n\r\n" +
                    ex.Message,
                    AppName,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
                return false;
            }
        }

        public static void WriteAppLog(string message)
        {
            try
            {
                string line = string.Format("[{0:yyyy-MM-dd HH:mm:ss}] {1}{2}", DateTime.Now, message, Environment.NewLine);
                File.AppendAllText(LogPath, line, Encoding.UTF8);
            }
            catch
            {
                // Ignore logging failures to prevent crash
            }
        }

        public static void ShowInfo(string message, string title = AppName)
        {
            MessageBox.Show(message, title, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        public static void ShowWarning(string message, string title = AppName)
        {
            MessageBox.Show(message, title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        public static void ShowErrorMessage(string message, string title = AppName)
        {
            MessageBox.Show(message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        public static bool ConfirmOperation(string message, string title = AppName)
        {
            DialogResult result = MessageBox.Show(message, title, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
            return result == DialogResult.Yes;
        }

        public static AppConfig GetApplicationConfig()
        {
            if (File.Exists(ConfigPath))
            {
                try
                {
                    string json = File.ReadAllText(ConfigPath, Encoding.UTF8);
                    JavaScriptSerializer serializer = new JavaScriptSerializer();
                    AppConfig config = serializer.Deserialize<AppConfig>(json);
                    if (config != null)
                    {
                        return config;
                    }
                }
                catch (Exception ex)
                {
                    WriteAppLog("設定ファイルの読み込みに失敗しました: " + ex.Message);
                }
            }

            AppConfig defaultConfig = new AppConfig();
            defaultConfig.EditorPath = ResolveDefaultEditorPath();
            defaultConfig.EditorArguments = "\"{file}\"";
            SaveApplicationConfig(defaultConfig);
            return defaultConfig;
        }

        public static void SaveApplicationConfig(AppConfig config)
        {
            try
            {
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                string json = serializer.Serialize(config);
                File.WriteAllText(ConfigPath, json, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                WriteAppLog("設定ファイルの保存に失敗しました: " + ex.Message);
            }
        }

        public static string ResolveDefaultEditorPath()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

            string[] candidates = new string[]
            {
                Path.Combine(
                    localAppData,
                    @"Programs\Microsoft VS Code\Code.exe"
                ),
                Path.Combine(
                    programFiles,
                    @"Microsoft VS Code\Code.exe"
                ),
                Path.Combine(
                    programFiles,
                    @"Notepad++\notepad++.exe"
                ),
                Path.Combine(
                    programFilesX86,
                    @"Notepad++\notepad++.exe"
                ),
                Path.Combine(
                    windowsDirectory,
                    @"System32\notepad.exe"
                ),
                Path.Combine(
                    windowsDirectory,
                    @"notepad.exe"
                )
            };

            foreach (string candidate in candidates)
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return Path.Combine(
                windowsDirectory,
                @"System32\notepad.exe"
            );
        }

        public static AppConfig GetConfiguredEditor(IWin32Window owner = null)
        {
            AppConfig config = GetApplicationConfig();
            if (string.IsNullOrEmpty(config.EditorPath) || !File.Exists(config.EditorPath))
            {
                DialogResult dr = MessageBox.Show(
                    "使用するテキストエディターが設定されていないか、見つかりません。\r\n設定画面を開きますか？",
                    AppName + " - エディター未設定",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning
                );

                if (dr == DialogResult.Yes)
                {
                    if (ShowEditorSettings(owner))
                    {
                        config = GetApplicationConfig();
                        if (!string.IsNullOrEmpty(config.EditorPath) && File.Exists(config.EditorPath))
                        {
                            return config;
                        }
                    }
                }
                return null;
            }
            return config;
        }

        public static bool ShowEditorSettings(IWin32Window owner)
        {
            using (EditorSettingsForm form = new EditorSettingsForm())
            {
                return form.ShowDialog(owner) == DialogResult.OK;
            }
        }

        public static bool LaunchConfiguredEditor(string filePath, IWin32Window owner = null)
        {
            AppConfig config = GetConfiguredEditor(owner);
            if (config == null) return false;

            try
            {
                string arguments;
                if (!string.IsNullOrEmpty(config.EditorArguments))
                {
                    if (config.EditorArguments.Contains("{file}"))
                    {
                        arguments = config.EditorArguments.Replace("{file}", filePath);
                    }
                    else if (config.EditorArguments.Contains("{0}"))
                    {
                        arguments = string.Format(config.EditorArguments, filePath);
                    }
                    else if (config.EditorArguments.Contains("%1"))
                    {
                        arguments = config.EditorArguments.Replace("%1", "\"" + filePath + "\"");
                    }
                    else
                    {
                        arguments = config.EditorArguments + " \"" + filePath + "\"";
                    }
                }
                else
                {
                    arguments = "\"" + filePath + "\"";
                }

                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = config.EditorPath;
                psi.Arguments = arguments;
                psi.UseShellExecute = false;

                Process proc = Process.Start(psi);
                if (proc == null)
                {
                    ShowErrorMessage("エディタープロセスの起動に失敗しました。");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                WriteAppLog("エディター起動エラー: " + ex.Message);
                ShowErrorMessage("エディターの起動中にエラーが発生しました:\r\n" + ex.Message);
                return false;
            }
        }

        public static bool OpenFileInConfiguredEditor(string filePath, IWin32Window owner = null)
        {
            return LaunchConfiguredEditor(filePath, owner);
        }

        public static string ConvertToNormalPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return string.Empty;

            string fullPath = Path.GetFullPath(path);
            string rootPath = Path.GetPathRoot(fullPath);

            if (string.Equals(fullPath, rootPath, StringComparison.OrdinalIgnoreCase))
            {
                return fullPath;
            }

            return fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        public static bool TestPathIsSame(string pathA, string pathB)
        {
            string normalA = ConvertToNormalPath(pathA);
            string normalB = ConvertToNormalPath(pathB);
            // Uses Ordinal comparison (case-sensitive) to detect case-only renames (file.txt -> File.txt)
            return string.Equals(normalA, normalB, StringComparison.Ordinal);
        }

        public static string NewEditorListFile(List<RenameItem> items, string prefix)
        {
            string timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
            string listFilePath = Path.Combine(WorkDirectory, string.Format("{0}-{1}.txt", prefix, timestamp));

            List<string> lines = new List<string>();
            lines.Add("# 左側のIDとタブは変更しないでください。右側のフルパスだけを編集します。");
            lines.Add("# 変更しないファイルは行ごと削除できます。行の並べ替えも可能です。");

            foreach (RenameItem item in items)
            {
                lines.Add(string.Format("{0}\t{1}", item.Id, item.DestinationPath));
            }

            UTF8Encoding utf8WithBom = new UTF8Encoding(true);
            File.WriteAllLines(listFilePath, lines.ToArray(), utf8WithBom);
            return listFilePath;
        }

        public static ParseResult ReadEditorListFile(string listFilePath, Dictionary<string, string> sourcePathMap)
        {
            ParseResult result = new ParseResult();
            if (!File.Exists(listFilePath))
            {
                result.Errors.Add("一覧ファイルが見つかりません。");
                return result;
            }

            string[] lines = File.ReadAllLines(listFilePath, new UTF8Encoding(true));
            Dictionary<string, bool> seenIds = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

            for (int index = 0; index < lines.Length; index++)
            {
                int lineNumber = index + 1;
                string line = lines[index].Trim();

                if (string.IsNullOrEmpty(line) || line.StartsWith("#"))
                {
                    continue;
                }

                int tabIndex = line.IndexOf('\t');
                if (tabIndex < 0)
                {
                    result.Errors.Add(
                        string.Format(
                            "行 {0}: タブ区切りが見つかりません。",
                            lineNumber
                        )
                    );
                    continue;
                }

                string id = line.Substring(
                    0,
                    tabIndex
                ).Trim();

                string destinationPath = line.Substring(
                    tabIndex + 1
                ).Trim();

                if (string.IsNullOrEmpty(id) || !sourcePathMap.ContainsKey(id))
                {
                    result.Errors.Add(string.Format("行 {0}: 未知のIDまたは不正なIDです: {1}", lineNumber, id));
                    continue;
                }

                if (seenIds.ContainsKey(id))
                {
                    result.Errors.Add(string.Format("行 {0}: IDが重複しています: {1}", lineNumber, id));
                    continue;
                }

                if (string.IsNullOrEmpty(destinationPath))
                {
                    result.Errors.Add(string.Format("行 {0}: 移動先のパスが空です: {1}", lineNumber, id));
                    continue;
                }

                try
                {
                    destinationPath = ConvertToNormalPath(destinationPath);
                }
                catch (Exception ex)
                {
                    result.Errors.Add(string.Format("行 {0}: 移動先のパスが不正です: {1}", lineNumber, ex.Message));
                    continue;
                }

                seenIds[id] = true;
                RenameItem item = new RenameItem();
                item.Id = id;
                item.SourcePath = sourcePathMap[id];
                item.DestinationPath = destinationPath;
                result.Items.Add(item);
            }

            return result;
        }

        public static bool ShowOperationPreview(List<RenameItem> items, string caption, IWin32Window owner = null)
        {
            List<RenameItem> changedItems = new List<RenameItem>();
            foreach (RenameItem item in items)
            {
                if (!TestPathIsSame(item.SourcePath, item.DestinationPath))
                {
                    changedItems.Add(item);
                }
            }

            if (changedItems.Count == 0)
            {
                ShowInfo("変更対象の項目はありません。");
                return false;
            }

            using (PreviewForm form = new PreviewForm(changedItems, caption))
            {
                return form.ShowDialog(owner) == DialogResult.OK;
            }
        }

        public static string NewTemporaryPath(string sourcePath)
        {
            string directory = Path.GetDirectoryName(sourcePath);
            string fileName = Path.GetFileName(sourcePath);
            string temporaryPath;

            do
            {
                string uniqueToken =
                    Guid.NewGuid().ToString("N");

                string temporaryName = string.Format(
                    ".__EditorRenameTool_{0}_{1}",
                    uniqueToken,
                    fileName
                );

                temporaryPath = Path.Combine(
                    directory,
                    temporaryName
                );
            }
            while (
                File.Exists(temporaryPath) ||
                Directory.Exists(temporaryPath)
            );

            return temporaryPath;
        }

        public static BatchResult InvokeRenameMoveBatch(List<RenameItem> items)
        {
            BatchResult result = new BatchResult();

            List<RenameItem> changedItems = new List<RenameItem>();
            foreach (RenameItem item in items)
            {
                if (!TestPathIsSame(item.SourcePath, item.DestinationPath))
                {
                    changedItems.Add(item);
                }
            }

            Dictionary<string, bool> sourcePathMap = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, bool> destinationPathMap = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            List<RenameItem> readyItems = new List<RenameItem>();

            foreach (RenameItem item in changedItems)
            {
                try
                {
                    string sourcePath = ConvertToNormalPath(item.SourcePath);
                    string destinationPath = ConvertToNormalPath(item.DestinationPath);
                    string sourceKey = sourcePath.ToLowerInvariant();

                    if (sourcePathMap.ContainsKey(sourceKey))
                    {
                        throw new InvalidOperationException("同じ元ファイルが複数回指定されています。");
                    }

                    sourcePathMap[sourceKey] = true;

                    RenameItem readyItem = new RenameItem();
                    readyItem.Id = item.Id;
                    readyItem.SourcePath = sourcePath;
                    readyItem.DestinationPath = destinationPath;
                    readyItem.TemporaryPath = null;
                    readyItem.IsStaged = false;
                    readyItems.Add(readyItem);
                }
                catch (Exception ex)
                {
                    RenameItem failed = new RenameItem();
                    failed.Id = item.Id;
                    failed.SourcePath = item.SourcePath;
                    failed.DestinationPath = item.DestinationPath;
                    failed.ErrorMessage = ex.Message;
                    result.Failed.Add(failed);
                }
            }

            List<RenameItem> validatedItems = new List<RenameItem>();
            foreach (RenameItem item in readyItems)
            {
                try
                {
                    string destinationKey = item.DestinationPath.ToLowerInvariant();
                    if (destinationPathMap.ContainsKey(destinationKey))
                    {
                        throw new InvalidOperationException("複数のファイルに同じ移動先が指定されています。");
                    }

                    destinationPathMap[destinationKey] = true;

                    if (!File.Exists(item.SourcePath))
                    {
                        throw new FileNotFoundException("元のファイルが存在しません。");
                    }

                    bool destinationExists = File.Exists(item.DestinationPath) || Directory.Exists(item.DestinationPath);
                    if (destinationExists && !sourcePathMap.ContainsKey(destinationKey))
                    {
                        throw new InvalidOperationException("移動先に同名のファイルまたはフォルダーが存在します。");
                    }

                    validatedItems.Add(item);
                }
                catch (Exception ex)
                {
                    RenameItem failed = new RenameItem();
                    failed.Id = item.Id;
                    failed.SourcePath = item.SourcePath;
                    failed.DestinationPath = item.DestinationPath;
                    failed.ErrorMessage = ex.Message;
                    result.Failed.Add(failed);
                }
            }

            // Stage 1: Temporary staging
            foreach (RenameItem item in validatedItems)
            {
                try
                {
                    string temporaryPath = NewTemporaryPath(item.SourcePath);
                    File.Move(item.SourcePath, temporaryPath);
                    item.TemporaryPath = temporaryPath;
                    item.IsStaged = true;
                }
                catch (Exception ex)
                {
                    item.IsStaged = false;
                    RenameItem failed = new RenameItem();
                    failed.Id = item.Id;
                    failed.SourcePath = item.SourcePath;
                    failed.DestinationPath = item.DestinationPath;
                    failed.ErrorMessage = "一時ファイルへの退避に失敗しました: " + ex.Message;
                    result.Failed.Add(failed);
                }
            }

            // Stage 2: Final Move
            foreach (RenameItem item in validatedItems)
            {
                if (!item.IsStaged) continue;

                try
                {
                    string destinationDirectory = Path.GetDirectoryName(item.DestinationPath);
                    if (!string.IsNullOrEmpty(destinationDirectory) && !Directory.Exists(destinationDirectory))
                    {
                        Directory.CreateDirectory(destinationDirectory);
                    }

                    if (
                        File.Exists(item.DestinationPath) ||
                        Directory.Exists(item.DestinationPath)
                    )
                    {
                        throw new InvalidOperationException(
                            "一時退避後に移動先が使用されています。"
                        );
                    }

                    File.Move(item.TemporaryPath, item.DestinationPath);

                    RenameItem success = new RenameItem();
                    success.Id = item.Id;
                    success.SourcePath = item.SourcePath;
                    success.DestinationPath = item.DestinationPath;
                    result.Success.Add(success);
                }
                catch (Exception ex)
                {
                    string moveError =
                        "最終移動に失敗しました: " +
                        ex.Message;

                    string currentSourcePath =
                        item.TemporaryPath;

                    try
                    {
                        if (File.Exists(item.TemporaryPath))
                        {
                            if (
                                File.Exists(item.SourcePath) ||
                                Directory.Exists(item.SourcePath)
                            )
                            {
                                throw new IOException(
                                    "元のパスが既に使用されています。"
                                );
                            }

                            File.Move(
                                item.TemporaryPath,
                                item.SourcePath
                            );

                            currentSourcePath =
                                item.SourcePath;
                        }
                    }
                    catch (Exception rollbackEx)
                    {
                        moveError +=
                            " 元の場所への復旧にも失敗しました: " +
                            rollbackEx.Message;

                        WriteAppLog(
                            string.Format(
                                "ロールバックに失敗しました: {0} - {1}",
                                item.TemporaryPath,
                                rollbackEx.Message
                            )
                        );
                    }

                    RenameItem failed = new RenameItem();
                    failed.Id = item.Id;
                    failed.SourcePath = currentSourcePath;
                    failed.DestinationPath =
                        item.DestinationPath;
                    failed.ErrorMessage = moveError;

                    result.Failed.Add(failed);
                }
            }

            return result;
        }

        public static string SaveRenameHistory(List<RenameItem> successfulItems, string mode, string relatedSessionId)
        {
            if (successfulItems == null || successfulItems.Count == 0) return null;

            string sessionId = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
            HistorySession session = new HistorySession();
            session.SchemaVersion = 1;
            session.SessionId = sessionId;
            session.Created = DateTime.Now.ToString("o");
            session.Mode = mode;
            session.RelatedSession = relatedSessionId ?? string.Empty;
            session.Undone = false;
            session.UndoneAt = null;

            foreach (RenameItem item in successfulItems)
            {
                HistoryEntry entry = new HistoryEntry();
                entry.Id = item.Id;
                entry.SourcePath = item.SourcePath;
                entry.DestinationPath = item.DestinationPath;
                session.Entries.Add(entry);
            }

            string historyFilePath = Path.Combine(HistoryDirectory, sessionId + ".json");
            try
            {
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                serializer.MaxJsonLength = int.MaxValue;
                string json = serializer.Serialize(session);
                File.WriteAllText(historyFilePath, json, Encoding.UTF8);
                return historyFilePath;
            }
            catch (Exception ex)
            {
                WriteAppLog("履歴の保存に失敗しました: " + ex.Message);
                return null;
            }
        }

        public static RetryResult EditAndRetryFailedItems(List<RenameItem> failedItems, string caption, string historyMode, string relatedSessionId, IWin32Window owner = null)
        {
            List<RenameItem> pendingItems = new List<RenameItem>(failedItems);

            while (pendingItems.Count > 0)
            {
                List<string> detailLines = new List<string>();
                int maxDetail = Math.Min(15, pendingItems.Count);
                for (int i = 0; i < maxDetail; i++)
                {
                    detailLines.Add(string.Format("{0}: {1}", pendingItems[i].Id, pendingItems[i].ErrorMessage));
                }
                if (pendingItems.Count > maxDetail)
                {
                    detailLines.Add(string.Format("ほか {0} 件", pendingItems.Count - maxDetail));
                }

                string message = string.Format(
                    "失敗: {0} 件\r\n\r\n{1}\r\n\r\n失敗した項目だけをエディターで開き、\r\n移動先を修正して再試行しますか？\r\n\r\n行を削除した項目は再試行しません。",
                    pendingItems.Count,
                    string.Join("\r\n", detailLines.ToArray())
                );

                if (!ConfirmOperation(message, caption))
                {
                    break;
                }

                Dictionary<string, string> sourcePathMap = new Dictionary<string, string>();
                List<RenameItem> editorItems = new List<RenameItem>();
                foreach (RenameItem fail in pendingItems)
                {
                    sourcePathMap[fail.Id] = fail.SourcePath;
                    RenameItem it = new RenameItem();
                    it.Id = fail.Id;
                    it.SourcePath = fail.SourcePath;
                    it.DestinationPath = fail.DestinationPath;
                    editorItems.Add(it);
                }

                string listFilePath = NewEditorListFile(editorItems, "retry");
                if (!LaunchConfiguredEditor(listFilePath, owner))
                {
                    break;
                }

                using (EditorWaitForm waitForm = new EditorWaitForm(listFilePath, editorItems.Count, caption))
                {
                    if (waitForm.ShowDialog(owner) != DialogResult.OK)
                    {
                        break;
                    }
                }

                ParseResult parsed = ReadEditorListFile(listFilePath, sourcePathMap);
                if (parsed.Errors.Count > 0)
                {
                    List<string> errorLines = new List<string>();
                    int maxErr = Math.Min(30, parsed.Errors.Count);
                    for (int i = 0; i < maxErr; i++)
                    {
                        errorLines.Add(parsed.Errors[i]);
                    }
                    if (parsed.Errors.Count > maxErr)
                    {
                        errorLines.Add(string.Format("ほか {0} 件", parsed.Errors.Count - maxErr));
                    }

                    ShowWarning("編集一覧にエラーがあります。\r\n\r\n" + string.Join("\r\n", errorLines.ToArray()));
                    continue;
                }

                if (!ShowOperationPreview(parsed.Items, "再試行予定", owner))
                {
                    if (ConfirmOperation("プレビューをキャンセルしました。\r\nエディターの編集を続けますか？\r\n\r\n［はい］編集に戻る\r\n［いいえ］再試行を中止する", AppName))
                    {
                        continue;
                    }
                    else
                    {
                        break;
                    }
                }

                BatchResult retryResult = InvokeRenameMoveBatch(parsed.Items);
                if (retryResult.Success.Count > 0)
                {
                    SaveRenameHistory(retryResult.Success, historyMode, relatedSessionId);
                }

                pendingItems = new List<RenameItem>(retryResult.Failed);

                ShowInfo(string.Format(
                    "再試行が完了しました。\r\n\r\n成功: {0} 件\r\n失敗: {1} 件",
                    retryResult.Success.Count,
                    pendingItems.Count
                ));
            }

            RetryResult result = new RetryResult();
            result.RemainingFailedItems = pendingItems;
            return result;
        }

        public static void StartRenameOperation(string[] selectedPaths, IWin32Window owner = null)
        {
            List<string> targetFiles = new List<string>();
            Dictionary<string, bool> registered = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

            foreach (string rawPath in selectedPaths)
            {
                if (!File.Exists(rawPath)) continue;

                try
                {
                    string normalized = ConvertToNormalPath(rawPath);
                    string key = normalized.ToUpperInvariant();
                    if (!registered.ContainsKey(key))
                    {
                        registered[key] = true;
                        targetFiles.Add(normalized);
                    }
                }
                catch (Exception ex)
                {
                    WriteAppLog("パス正規化失敗: " + rawPath + " - " + ex.Message);
                }
            }

            if (targetFiles.Count == 0)
            {
                ShowWarning("名前変更または移動の対象となるファイルが選択されていません。");
                return;
            }

            AppConfig config = GetConfiguredEditor(owner);
            if (config == null) return;

            List<RenameItem> items = new List<RenameItem>();
            Dictionary<string, string> sourcePathMap = new Dictionary<string, string>();
            int counter = 1;

            foreach (string filePath in targetFiles)
            {
                string id = string.Format("F{0:D8}", counter++);
                RenameItem item = new RenameItem();
                item.Id = id;
                item.SourcePath = filePath;
                item.DestinationPath = filePath;
                items.Add(item);
                sourcePathMap[id] = filePath;
            }

            string listFilePath = NewEditorListFile(items, "rename");

            while (true)
            {
                if (!LaunchConfiguredEditor(listFilePath, owner))
                {
                    return;
                }

                using (EditorWaitForm waitForm = new EditorWaitForm(listFilePath, items.Count, "編集待機中"))
                {
                    waitForm.OnFilesDropped = delegate(string[] droppedPaths)
                    {
                        if (droppedPaths == null || droppedPaths.Length == 0) return;

                        List<string> addedPaths = new List<string>();
                        foreach (string raw in droppedPaths)
                        {
                            if (File.Exists(raw))
                            {
                                try
                                {
                                    string norm = ConvertToNormalPath(raw);
                                    string key = norm.ToUpperInvariant();
                                    if (!registered.ContainsKey(key))
                                    {
                                        registered[key] = true;
                                        addedPaths.Add(norm);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    WriteAppLog("パス正規化失敗: " + raw + " - " + ex.Message);
                                }
                            }
                            else if (Directory.Exists(raw))
                            {
                                try
                                {
                                    string[] dirFiles = Directory.GetFiles(raw, "*", SearchOption.AllDirectories);
                                    foreach (string df in dirFiles)
                                    {
                                        string norm = ConvertToNormalPath(df);
                                        string key = norm.ToUpperInvariant();
                                        if (!registered.ContainsKey(key))
                                        {
                                            registered[key] = true;
                                            addedPaths.Add(norm);
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    WriteAppLog("フォルダー内ファイル取得失敗: " + raw + " - " + ex.Message);
                                }
                            }
                        }

                        if (addedPaths.Count > 0)
                        {
                            List<string> appendLines = new List<string>();
                            foreach (string p in addedPaths)
                            {
                                string id = string.Format("F{0:D8}", counter++);
                                RenameItem item = new RenameItem();
                                item.Id = id;
                                item.SourcePath = p;
                                item.DestinationPath = p;
                                items.Add(item);
                                sourcePathMap[id] = p;
                                appendLines.Add(string.Format("{0}\t{1}", id, p));
                            }

                            try
                            {
                                File.AppendAllLines(listFilePath, appendLines.ToArray(), new UTF8Encoding(true));
                                waitForm.UpdateItemCount(items.Count);
                                ShowInfo(string.Format(
                                    "{0} 件のファイルを追加しました（計 {1} 件）。\r\n\r\nエディター側の一覧を再読み込みするか、\r\n［エディターを再度開く］ボタンを押して編集を続けてください。",
                                    addedPaths.Count,
                                    items.Count
                                ));
                            }
                            catch (Exception ex)
                            {
                                ShowErrorMessage("一覧ファイルへの追記に失敗しました:\r\n" + ex.Message);
                            }
                        }
                        else
                        {
                            ShowInfo("追加対象となる新しいファイルはありませんでした（既に登録済みか無効なファイルです）。");
                        }
                    };

                    if (waitForm.ShowDialog(owner) != DialogResult.OK)
                    {
                        return;
                    }
                }

                ParseResult parsed = ReadEditorListFile(listFilePath, sourcePathMap);
                if (parsed.Errors.Count > 0)
                {
                    List<string> errorLines = new List<string>();
                    int maxErr = Math.Min(30, parsed.Errors.Count);
                    for (int i = 0; i < maxErr; i++) errorLines.Add(parsed.Errors[i]);
                    if (parsed.Errors.Count > maxErr) errorLines.Add(string.Format("ほか {0} 件", parsed.Errors.Count - maxErr));

                    string errMsg = string.Format(
                        "編集一覧にエラーがあります。\r\n\r\n{0}\r\n\r\nエディターを再度開いて修正しますか？",
                        string.Join("\r\n", errorLines.ToArray())
                    );

                    if (ConfirmOperation(errMsg, AppName + " - 編集一覧のエラー"))
                    {
                        continue;
                    }
                    else
                    {
                        return;
                    }
                }

                if (!ShowOperationPreview(parsed.Items, "実行予定", owner))
                {
                    if (ConfirmOperation("プレビューをキャンセルしました。\r\nエディターの編集を続けますか？\r\n\r\n［はい］編集に戻る\r\n［いいえ］処理を中止する", AppName))
                    {
                        continue;
                    }
                    else
                    {
                        return;
                    }
                }

                BatchResult batchResult = InvokeRenameMoveBatch(parsed.Items);
                string sessionId = string.Empty;

                if (batchResult.Success.Count > 0)
                {
                    string hPath = SaveRenameHistory(batchResult.Success, "Rename", string.Empty);
                    if (!string.IsNullOrEmpty(hPath))
                    {
                        sessionId = Path.GetFileNameWithoutExtension(hPath);
                    }
                }

                string summary = string.Format(
                    "処理が完了しました。\r\n\r\n成功: {0} 件\r\n失敗: {1} 件",
                    batchResult.Success.Count,
                    batchResult.Failed.Count
                );

                if (batchResult.Failed.Count == 0)
                {
                    ShowInfo(summary);
                    return;
                }
                else
                {
                    ShowWarning(summary);
                    EditAndRetryFailedItems(batchResult.Failed, "失敗項目の再試行", "Retry", sessionId, owner);
                    return;
                }
            }
        }

        public static List<HistorySession> GetRenameHistorySessions()
        {
            List<HistorySession> sessions = new List<HistorySession>();
            if (!Directory.Exists(HistoryDirectory)) return sessions;

            string[] files = Directory.GetFiles(HistoryDirectory, "*.json");
            Array.Sort(files);
            Array.Reverse(files);

            JavaScriptSerializer serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = int.MaxValue;

            foreach (string file in files)
            {
                try
                {
                    string json = File.ReadAllText(file, Encoding.UTF8);
                    HistorySession content = serializer.Deserialize<HistorySession>(json);

                    if (content != null && content.Entries != null &&
                        (string.Equals(content.Mode, "Rename", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(content.Mode, "Retry", StringComparison.OrdinalIgnoreCase)))
                    {
                        content.FilePath = file;
                        sessions.Add(content);
                    }
                }
                catch (Exception ex)
                {
                    WriteAppLog("履歴ファイル読み込み失敗: " + file + " - " + ex.Message);
                }
            }

            return sessions;
        }

        public static void SetSessionUndone(HistorySession session)
        {
            try
            {
                if (string.IsNullOrEmpty(session.FilePath) || !File.Exists(session.FilePath)) return;

                string json = File.ReadAllText(session.FilePath, Encoding.UTF8);
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                serializer.MaxJsonLength = int.MaxValue;

                HistorySession raw = serializer.Deserialize<HistorySession>(json);
                if (raw != null)
                {
                    string undoneAt = DateTime.Now.ToString("o");
                    raw.Undone = true;
                    raw.UndoneAt = undoneAt;

                    string updatedJson = serializer.Serialize(raw);
                    File.WriteAllText(session.FilePath, updatedJson, Encoding.UTF8);

                    session.Undone = true;
                    session.UndoneAt = undoneAt;
                }
            }
            catch (Exception ex)
            {
                WriteAppLog("セッションのUndoフラグ更新に失敗しました: " + ex.Message);
            }
        }

        public static void StartUndoOperation(IWin32Window owner)
        {
            List<HistorySession> sessions = GetRenameHistorySessions();
            if (sessions.Count == 0)
            {
                ShowInfo("元に戻せる履歴が存在しません。");
                return;
            }

            using (UndoForm form = new UndoForm())
            {
                form.ShowDialog(owner);
            }
        }

        public static bool InstallShortcut(bool showMessage)
        {
            try
            {
                string sendTo = Environment.GetFolderPath(Environment.SpecialFolder.SendTo);
                string shortcutPath = Path.Combine(sendTo, "Editor Rename Tool.lnk");
                string exePath = Process.GetCurrentProcess().MainModule.FileName;

                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null)
                {
                    if (showMessage) ShowErrorMessage("WScript.Shell が見つかりませんでした。");
                    return false;
                }

                object shell = Activator.CreateInstance(shellType);
                object shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { shortcutPath });
                Type scType = shortcut.GetType();
                scType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { exePath });
                scType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { Path.GetDirectoryName(exePath) });
                scType.InvokeMember("Description", BindingFlags.SetProperty, null, shortcut, new object[] { "Editor Rename Tool - 送る" });
                scType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);

                if (showMessage)
                {
                    ShowInfo("エクスプローラーの「送る」メニューにショートカットを作成しました:\r\n" + shortcutPath);
                }
                return true;
            }
            catch (Exception ex)
            {
                WriteAppLog("ショートカット作成失敗: " + ex.Message);
                if (showMessage) ShowErrorMessage("ショートカットの作成に失敗しました:\r\n" + ex.Message);
                return false;
            }
        }

        public static bool UninstallShortcut(bool showMessage)
        {
            try
            {
                string sendTo = Environment.GetFolderPath(Environment.SpecialFolder.SendTo);
                string shortcutPath = Path.Combine(sendTo, "Editor Rename Tool.lnk");
                bool existed = false;
                if (File.Exists(shortcutPath))
                {
                    File.Delete(shortcutPath);
                    existed = true;
                }

                if (showMessage)
                {
                    if (existed)
                    {
                        ShowInfo("「送る」メニューのショートカットを削除しました。");
                    }
                    else
                    {
                        ShowInfo("削除対象のショートカットは見つかりませんでした。");
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                WriteAppLog("ショートカット削除失敗: " + ex.Message);
                if (showMessage) ShowErrorMessage("ショートカットの削除に失敗しました:\r\n" + ex.Message);
                return false;
            }
        }
    }

    public class MainForm : Form
    {
        private List<string> targetFiles;
        private Dictionary<string, bool> registeredPaths;

        private ListBox fileListBox;
        private Label lblCount;
        private CheckBox chkSubfolders;
        private Button btnStartRename;
        private Button btnAddFiles;
        private Button btnAddFolder;
        private Button btnRemoveSelected;
        private Button btnClear;

        public MainForm()
        {
            targetFiles = new List<string>();
            registeredPaths = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.Text = Program.AppName;
            this.Size = new Size(730, 560);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
            this.AllowDrop = true;

            DragEventHandler dragEnterHandler = delegate(object sender, DragEventArgs e)
            {
                if (e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    e.Effect = DragDropEffects.Copy;
                }
                else
                {
                    e.Effect = DragDropEffects.None;
                }
            };

            DragEventHandler dragDropHandler = delegate(object sender, DragEventArgs e)
            {
                if (e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    string[] dropped = (string[])e.Data.GetData(DataFormats.FileDrop);
                    if (dropped != null && dropped.Length > 0)
                    {
                        AddPaths(dropped, chkSubfolders.Checked);
                    }
                }
            };

            this.DragEnter += dragEnterHandler;
            this.DragDrop += dragDropHandler;

            Label titleLabel = new Label();
            titleLabel.Text = Program.AppName;
            titleLabel.Font = new Font("Segoe UI", 14f, FontStyle.Bold);
            titleLabel.Location = new Point(25, 15);
            titleLabel.AutoSize = true;

            Label descLabel = new Label();
            descLabel.Text = "変更したいファイルをここにドラッグ＆ドロップして追加してください。\r\n「送る」メニューのパス長上限を超える大量のファイル（数千件以上）も一括登録できます。";
            descLabel.Location = new Point(27, 46);
            descLabel.Size = new Size(660, 38);

            fileListBox = new ListBox();
            fileListBox.Location = new Point(25, 90);
            fileListBox.Size = new Size(515, 230);
            fileListBox.HorizontalScrollbar = true;
            fileListBox.SelectionMode = SelectionMode.MultiExtended;
            fileListBox.AllowDrop = true;
            fileListBox.DragEnter += dragEnterHandler;
            fileListBox.DragDrop += dragDropHandler;
            fileListBox.SelectedIndexChanged += delegate
            {
                btnRemoveSelected.Enabled = (fileListBox.SelectedIndices.Count > 0);
            };

            btnStartRename = new Button();
            btnStartRename.Text = "エディターで\r\n名前変更を開始";
            btnStartRename.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            btnStartRename.Location = new Point(555, 90);
            btnStartRename.Size = new Size(140, 52);
            btnStartRename.Enabled = false;
            btnStartRename.Click += delegate { StartRename(); };

            btnAddFiles = new Button();
            btnAddFiles.Text = "ファイル追加...";
            btnAddFiles.Location = new Point(555, 152);
            btnAddFiles.Size = new Size(140, 32);
            btnAddFiles.Click += delegate
            {
                using (OpenFileDialog ofd = new OpenFileDialog())
                {
                    ofd.Multiselect = true;
                    ofd.Title = "名前変更対象のファイルを選択";
                    ofd.Filter = "すべてのファイル (*.*)|*.*";
                    if (ofd.ShowDialog(this) == DialogResult.OK)
                    {
                        AddPaths(ofd.FileNames, false);
                    }
                }
            };

            btnAddFolder = new Button();
            btnAddFolder.Text = "フォルダー追加...";
            btnAddFolder.Location = new Point(555, 192);
            btnAddFolder.Size = new Size(140, 32);
            btnAddFolder.Click += delegate
            {
                using (FolderBrowserDialog fbd = new FolderBrowserDialog())
                {
                    fbd.Description = "対象ファイルを含むフォルダーを選択してください。";
                    fbd.ShowNewFolderButton = false;
                    if (fbd.ShowDialog(this) == DialogResult.OK)
                    {
                        AddPaths(new string[] { fbd.SelectedPath }, chkSubfolders.Checked);
                    }
                }
            };

            btnRemoveSelected = new Button();
            btnRemoveSelected.Text = "選択項目を削除";
            btnRemoveSelected.Location = new Point(555, 232);
            btnRemoveSelected.Size = new Size(140, 32);
            btnRemoveSelected.Enabled = false;
            btnRemoveSelected.Click += delegate { RemoveSelectedItems(); };

            btnClear = new Button();
            btnClear.Text = "一覧をクリア";
            btnClear.Location = new Point(555, 272);
            btnClear.Size = new Size(140, 32);
            btnClear.Enabled = false;
            btnClear.Click += delegate { ClearItems(); };

            lblCount = new Label();
            lblCount.Text = "対象ファイル: 0 件";
            lblCount.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            lblCount.Location = new Point(25, 332);
            lblCount.Size = new Size(200, 22);

            chkSubfolders = new CheckBox();
            chkSubfolders.Text = "フォルダー投入時にサブフォルダーも含める";
            chkSubfolders.Location = new Point(240, 331);
            chkSubfolders.Size = new Size(300, 22);
            chkSubfolders.Checked = true;

            GroupBox toolsGroup = new GroupBox();
            toolsGroup.Text = "ツール・設定";
            toolsGroup.Location = new Point(25, 365);
            toolsGroup.Size = new Size(670, 135);

            Button btnUndo = new Button();
            btnUndo.Text = "Undo履歴を開く...";
            btnUndo.Location = new Point(20, 30);
            btnUndo.Size = new Size(150, 36);
            btnUndo.Click += delegate { Program.StartUndoOperation(this); };

            Button btnSettings = new Button();
            btnSettings.Text = "エディター設定...";
            btnSettings.Location = new Point(180, 30);
            btnSettings.Size = new Size(150, 36);
            btnSettings.Click += delegate { Program.ShowEditorSettings(this); };

            Button btnShortcut = new Button();
            btnShortcut.Text = "「送る」にショートカット作成";
            btnShortcut.Location = new Point(340, 30);
            btnShortcut.Size = new Size(180, 36);
            btnShortcut.Click += delegate { Program.InstallShortcut(true); };

            Button btnOpenFolder = new Button();
            btnOpenFolder.Text = "作業フォルダー";
            btnOpenFolder.Location = new Point(530, 30);
            btnOpenFolder.Size = new Size(120, 36);
            btnOpenFolder.Click += delegate
            {
                try { Process.Start("explorer.exe", Program.AppDirectory); }
                catch (Exception ex) { Program.ShowErrorMessage(ex.Message); }
            };

            Button btnOpenLog = new Button();
            btnOpenLog.Text = "ログを開く";
            btnOpenLog.Location = new Point(20, 80);
            btnOpenLog.Size = new Size(150, 36);
            btnOpenLog.Click += delegate
            {
                try
                {
                    if (File.Exists(Program.LogPath))
                    {
                        Process.Start("notepad.exe", Program.LogPath);
                    }
                    else
                    {
                        Program.ShowInfo("ログファイルはまだ作成されていません。");
                    }
                }
                catch (Exception ex) { Program.ShowErrorMessage(ex.Message); }
            };

            Button btnClose = new Button();
            btnClose.Text = "閉じる";
            btnClose.Location = new Point(530, 80);
            btnClose.Size = new Size(120, 36);
            btnClose.Click += delegate { this.Close(); };

            toolsGroup.Controls.Add(btnUndo);
            toolsGroup.Controls.Add(btnSettings);
            toolsGroup.Controls.Add(btnShortcut);
            toolsGroup.Controls.Add(btnOpenFolder);
            toolsGroup.Controls.Add(btnOpenLog);
            toolsGroup.Controls.Add(btnClose);

            this.Controls.Add(titleLabel);
            this.Controls.Add(descLabel);
            this.Controls.Add(fileListBox);
            this.Controls.Add(btnStartRename);
            this.Controls.Add(btnAddFiles);
            this.Controls.Add(btnAddFolder);
            this.Controls.Add(btnRemoveSelected);
            this.Controls.Add(btnClear);
            this.Controls.Add(lblCount);
            this.Controls.Add(chkSubfolders);
            this.Controls.Add(toolsGroup);
        }

        private void AddPaths(string[] paths, bool includeSubfolders)
        {
            if (paths == null || paths.Length == 0) return;

            int addedCount = 0;
            this.Cursor = Cursors.WaitCursor;
            fileListBox.BeginUpdate();
            try
            {
                foreach (string rawPath in paths)
                {
                    if (string.IsNullOrEmpty(rawPath)) continue;

                    if (File.Exists(rawPath))
                    {
                        if (AddSingleFile(rawPath)) addedCount++;
                    }
                    else if (Directory.Exists(rawPath))
                    {
                        SearchOption option = includeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                        try
                        {
                            string[] dirFiles = Directory.GetFiles(rawPath, "*", option);
                            foreach (string df in dirFiles)
                            {
                                if (AddSingleFile(df)) addedCount++;
                            }
                        }
                        catch (Exception ex)
                        {
                            Program.WriteAppLog("フォルダー内ファイル取得失敗: " + rawPath + " - " + ex.Message);
                        }
                    }
                }
            }
            finally
            {
                fileListBox.EndUpdate();
                this.Cursor = Cursors.Default;
                UpdateUIState();
            }
        }

        private bool AddSingleFile(string rawPath)
        {
            try
            {
                string normalized = Program.ConvertToNormalPath(rawPath);
                string key = normalized.ToUpperInvariant();
                if (!registeredPaths.ContainsKey(key))
                {
                    registeredPaths[key] = true;
                    targetFiles.Add(normalized);
                    fileListBox.Items.Add(normalized);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Program.WriteAppLog("パス正規化失敗: " + rawPath + " - " + ex.Message);
            }
            return false;
        }

        private void RemoveSelectedItems()
        {
            if (fileListBox.SelectedIndices.Count == 0) return;

            List<int> indices = new List<int>();
            foreach (int idx in fileListBox.SelectedIndices)
            {
                indices.Add(idx);
            }
            indices.Sort();
            indices.Reverse();

            fileListBox.BeginUpdate();
            try
            {
                foreach (int idx in indices)
                {
                    if (idx >= 0 && idx < targetFiles.Count)
                    {
                        string norm = targetFiles[idx];
                        registeredPaths.Remove(norm.ToUpperInvariant());
                        targetFiles.RemoveAt(idx);
                        fileListBox.Items.RemoveAt(idx);
                    }
                }
            }
            finally
            {
                fileListBox.EndUpdate();
                UpdateUIState();
            }
        }

        private void ClearItems()
        {
            registeredPaths.Clear();
            targetFiles.Clear();
            fileListBox.Items.Clear();
            UpdateUIState();
        }

        private void UpdateUIState()
        {
            lblCount.Text = string.Format("対象ファイル: {0} 件", targetFiles.Count);
            btnStartRename.Enabled = (targetFiles.Count > 0);
            btnClear.Enabled = (targetFiles.Count > 0);
            btnRemoveSelected.Enabled = (fileListBox.SelectedIndices.Count > 0);
        }

        private void StartRename()
        {
            if (targetFiles.Count == 0)
            {
                Program.ShowWarning("名前変更の対象となるファイルが追加されていません。\r\nファイルをドラッグ＆ドロップするか、［ファイル追加...］から選択してください。");
                return;
            }

            Program.StartRenameOperation(targetFiles.ToArray(), this);
            ClearItems();
        }
    }

    public class EditorWaitForm : Form
    {
        private string listFilePath;
        private int itemsCount;
        private Label infoLabel;

        public Action<string[]> OnFilesDropped { get; set; }

        public EditorWaitForm(string listFilePath, int itemsCount, string caption)
        {
            this.listFilePath = listFilePath;
            this.itemsCount = itemsCount;
            InitializeComponent(caption);
        }

        public void UpdateItemCount(int newCount)
        {
            this.itemsCount = newCount;
            if (infoLabel != null)
            {
                infoLabel.Text = string.Format("対象件数: {0} 件   ファイル: {1}", itemsCount, Path.GetFileName(listFilePath));
            }
        }

        private void InitializeComponent(string caption)
        {
            this.Text = string.Format("{0} - {1}", Program.AppName, caption);
            this.Size = new Size(620, 290);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
            this.AllowDrop = true;

            this.DragEnter += delegate(object sender, DragEventArgs e)
            {
                if (e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    e.Effect = DragDropEffects.Copy;
                }
                else
                {
                    e.Effect = DragDropEffects.None;
                }
            };

            this.DragDrop += delegate(object sender, DragEventArgs e)
            {
                if (e.Data.GetDataPresent(DataFormats.FileDrop) && OnFilesDropped != null)
                {
                    string[] paths = (string[])e.Data.GetData(DataFormats.FileDrop);
                    if (paths != null && paths.Length > 0)
                    {
                        OnFilesDropped(paths);
                    }
                }
            };

            Label titleLabel = new Label();
            titleLabel.Text = "エディターで一覧を編集してください";
            titleLabel.Font = new Font("Segoe UI", 12f, FontStyle.Bold);
            titleLabel.Location = new Point(25, 18);
            titleLabel.AutoSize = true;

            Label descLabel = new Label();
            descLabel.Text = "エディターで右側のファイルパスを編集・保存してください。\r\nこの画面にファイルをドロップして、対象ファイルを後から追加することもできます。\r\n保存完了後、下の［編集完了（プレビューへ進む）］ボタンを押してください。";
            descLabel.Location = new Point(27, 50);
            descLabel.Size = new Size(555, 50);

            infoLabel = new Label();
            infoLabel.Text = string.Format("対象件数: {0} 件   ファイル: {1}", itemsCount, Path.GetFileName(listFilePath));
            infoLabel.ForeColor = SystemColors.GrayText;
            infoLabel.Location = new Point(27, 108);
            infoLabel.Size = new Size(555, 22);

            Button btnComplete = new Button();
            btnComplete.Text = "編集完了（プレビューへ進む）";
            btnComplete.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            btnComplete.Location = new Point(25, 150);
            btnComplete.Size = new Size(230, 42);
            btnComplete.DialogResult = DialogResult.OK;

            Button btnReopen = new Button();
            btnReopen.Text = "エディターを再度開く";
            btnReopen.Location = new Point(270, 150);
            btnReopen.Size = new Size(160, 42);
            btnReopen.Click += delegate
            {
                Program.LaunchConfiguredEditor(listFilePath, this);
            };

            Button btnCancel = new Button();
            btnCancel.Text = "キャンセル";
            btnCancel.Location = new Point(445, 150);
            btnCancel.Size = new Size(95, 42);
            btnCancel.DialogResult = DialogResult.Cancel;

            this.Controls.Add(titleLabel);
            this.Controls.Add(descLabel);
            this.Controls.Add(infoLabel);
            this.Controls.Add(btnComplete);
            this.Controls.Add(btnReopen);
            this.Controls.Add(btnCancel);

            this.AcceptButton = btnComplete;
            this.CancelButton = btnCancel;
        }
    }

    public class PreviewForm : Form
    {
        private List<RenameItem> items;
        private ListBox previewListBox;
        private Font fontPath;
        private Font fontTag;
        private Font fontId;

        public PreviewForm(List<RenameItem> items, string caption)
        {
            this.items = items;
            InitializeComponent(caption);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (fontPath != null) fontPath.Dispose();
                if (fontTag != null) fontTag.Dispose();
                if (fontId != null) fontId.Dispose();
            }
            base.Dispose(disposing);
        }

        private void InitializeComponent(string caption)
        {
            this.Text = string.Format("{0} - {1}", Program.AppName, caption);
            this.Font = new Font("Segoe UI", 10f, FontStyle.Regular);
            this.ClientSize = new Size(960, 600);
            this.MinimumSize = new Size(760, 480);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.Sizable;

            fontPath = new Font("Segoe UI", 11.5f, FontStyle.Regular);
            fontTag = new Font("Segoe UI", 10.5f, FontStyle.Bold);
            fontId = new Font("Consolas", 10f, FontStyle.Regular);

            int clientW = this.ClientSize.Width;
            int clientH = this.ClientSize.Height;

            int btnHeight = 36;
            int bottomMargin = 16;
            int btnY = clientH - bottomMargin - btnHeight;
            int listTop = 44;
            int listGap = 12;
            int listHeight = btnY - listGap - listTop;

            Label infoLabel = new Label();
            infoLabel.Text = string.Format("変更予定件数: {0} 件 （変更前と変更後を確認し、よろしければ［実行］を押してください）", items.Count);
            infoLabel.Font = new Font("Segoe UI", 11f, FontStyle.Bold);
            infoLabel.Location = new Point(16, 14);
            infoLabel.AutoSize = true;

            previewListBox = new ListBox();
            previewListBox.Location = new Point(16, listTop);
            previewListBox.Size = new Size(clientW - 32, listHeight);
            previewListBox.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            previewListBox.DrawMode = DrawMode.OwnerDrawFixed;
            previewListBox.ItemHeight = 58;
            previewListBox.IntegralHeight = false;
            previewListBox.HorizontalScrollbar = true;

            try
            {
                PropertyInfo doubleBufferedProp = typeof(Control).GetProperty("DoubleBuffered", BindingFlags.NonPublic | BindingFlags.Instance);
                if (doubleBufferedProp != null)
                {
                    doubleBufferedProp.SetValue(previewListBox, true, null);
                }
            }
            catch {}

            int maxPixelWidth = 0;
            using (Graphics g = this.CreateGraphics())
            {
                foreach (RenameItem item in items)
                {
                    previewListBox.Items.Add(item);
                    int w1 = (int)g.MeasureString(item.SourcePath ?? string.Empty, fontPath).Width;
                    int w2 = (int)g.MeasureString(item.DestinationPath ?? string.Empty, fontPath).Width;
                    int w = Math.Max(w1, w2);
                    if (w > maxPixelWidth) maxPixelWidth = w;
                }
            }
            previewListBox.HorizontalExtent = Math.Max(previewListBox.Width, maxPixelWidth + 240);

            previewListBox.DrawItem += delegate(object sender, DrawItemEventArgs e)
            {
                if (e.Index < 0 || e.Index >= items.Count) return;

                RenameItem item = items[e.Index];
                bool isSelected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;

                Color backColor;
                if (isSelected)
                {
                    backColor = Color.FromArgb(220, 238, 255);
                }
                else if (e.Index % 2 == 1)
                {
                    backColor = Color.FromArgb(250, 251, 253);
                }
                else
                {
                    backColor = Color.White;
                }

                using (SolidBrush backBrush = new SolidBrush(backColor))
                {
                    e.Graphics.FillRectangle(backBrush, e.Bounds);
                }

                int y1 = e.Bounds.Y + 6;
                int y2 = e.Bounds.Y + 31;

                int xId = e.Bounds.X + 10;
                int xTag = xId + 92;
                int xPath = xTag + 68;

                using (SolidBrush idBrush = new SolidBrush(isSelected ? Color.FromArgb(30, 60, 110) : Color.FromArgb(100, 105, 115)))
                {
                    e.Graphics.DrawString(string.Format("[{0}]", item.Id), fontId, idBrush, xId, y1 + 2);
                }

                using (SolidBrush beforeTagBrush = new SolidBrush(Color.FromArgb(195, 30, 30)))
                using (SolidBrush beforePathBrush = new SolidBrush(Color.FromArgb(55, 65, 81)))
                {
                    e.Graphics.DrawString("変更前:", fontTag, beforeTagBrush, xTag, y1);
                    e.Graphics.DrawString(item.SourcePath ?? string.Empty, fontPath, beforePathBrush, xPath, y1);
                }

                using (SolidBrush afterTagBrush = new SolidBrush(Color.FromArgb(16, 135, 60)))
                using (SolidBrush afterPathBrush = new SolidBrush(Color.FromArgb(15, 23, 42)))
                {
                    e.Graphics.DrawString("変更後:", fontTag, afterTagBrush, xTag, y2);
                    e.Graphics.DrawString(item.DestinationPath ?? string.Empty, fontPath, afterPathBrush, xPath, y2);
                }

                using (Pen linePen = new Pen(Color.FromArgb(226, 232, 240)))
                {
                    e.Graphics.DrawLine(linePen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
                }

                if (isSelected)
                {
                    using (Pen selPen = new Pen(Color.FromArgb(60, 130, 230)))
                    {
                        e.Graphics.DrawRectangle(selPen, e.Bounds.X, e.Bounds.Y, e.Bounds.Width - 1, e.Bounds.Height - 1);
                    }
                }
            };

            ContextMenuStrip contextMenu = new ContextMenuStrip();
            ToolStripMenuItem menuCopySelected = new ToolStripMenuItem("選択項目のパスをコピー (Ctrl+C)");
            menuCopySelected.Click += delegate
            {
                CopySelectedItemToClipboard();
            };
            ToolStripMenuItem menuCopyAll = new ToolStripMenuItem("全件の変更予定をコピー");
            menuCopyAll.Click += delegate
            {
                CopyAllItemsToClipboard();
            };
            contextMenu.Items.Add(menuCopySelected);
            contextMenu.Items.Add(menuCopyAll);
            previewListBox.ContextMenuStrip = contextMenu;

            previewListBox.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Control && e.KeyCode == Keys.C)
                {
                    CopySelectedItemToClipboard();
                    e.Handled = true;
                }
            };

            Button btnCopy = new Button();
            btnCopy.Text = "パスをコピー";
            btnCopy.Location = new Point(16, btnY);
            btnCopy.Size = new Size(130, btnHeight);
            btnCopy.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            btnCopy.Click += delegate { CopySelectedItemToClipboard(); };

            Button btnCancel = new Button();
            btnCancel.Text = "キャンセル";
            btnCancel.DialogResult = DialogResult.Cancel;
            btnCancel.Size = new Size(100, btnHeight);
            btnCancel.Location = new Point(clientW - bottomMargin - btnCancel.Width, btnY);
            btnCancel.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;

            Button btnExecute = new Button();
            btnExecute.Text = "実行";
            btnExecute.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
            btnExecute.DialogResult = DialogResult.OK;
            btnExecute.Size = new Size(115, btnHeight);
            btnExecute.Location = new Point(btnCancel.Left - 10 - btnExecute.Width, btnY);
            btnExecute.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;

            this.Controls.Add(infoLabel);
            this.Controls.Add(previewListBox);
            this.Controls.Add(btnCopy);
            this.Controls.Add(btnExecute);
            this.Controls.Add(btnCancel);

            this.AcceptButton = btnExecute;
            this.CancelButton = btnCancel;
        }

        private void CopySelectedItemToClipboard()
        {
            int index = previewListBox.SelectedIndex;
            if (index < 0 || index >= items.Count) return;

            RenameItem item = items[index];
            string text = string.Format("[{0}]\r\n変更前: {1}\r\n変更後: {2}", item.Id, item.SourcePath, item.DestinationPath);
            try
            {
                Clipboard.SetText(text);
            }
            catch {}
        }

        private void CopyAllItemsToClipboard()
        {
            if (items.Count == 0) return;

            List<string> lines = new List<string>();
            foreach (RenameItem item in items)
            {
                lines.Add(string.Format("[{0}]", item.Id));
                lines.Add(string.Format("変更前: {0}", item.SourcePath));
                lines.Add(string.Format("変更後: {0}", item.DestinationPath));
                lines.Add(string.Empty);
            }
            try
            {
                Clipboard.SetText(string.Join("\r\n", lines.ToArray()));
            }
            catch {}
        }
    }

    public class EditorSettingsForm : Form
    {
        private TextBox txtPath;
        private TextBox txtArgs;

        public EditorSettingsForm()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.Text = Program.AppName + " - エディター設定";
            this.Size = new Size(580, 270);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);

            AppConfig config = Program.GetApplicationConfig();

            Label lblPath = new Label();
            lblPath.Text = "エディター実行ファイル:";
            lblPath.Location = new Point(20, 20);
            lblPath.AutoSize = true;

            txtPath = new TextBox();
            txtPath.Location = new Point(20, 45);
            txtPath.Size = new Size(430, 26);
            txtPath.Text = config.EditorPath ?? string.Empty;

            Button btnBrowse = new Button();
            btnBrowse.Text = "参照...";
            btnBrowse.Location = new Point(460, 43);
            btnBrowse.Size = new Size(85, 30);
            btnBrowse.Click += delegate
            {
                using (OpenFileDialog ofd = new OpenFileDialog())
                {
                    ofd.Filter = "実行可能ファイル (*.exe)|*.exe|すべてのファイル (*.*)|*.*";
                    ofd.Title = "テキストエディターの選択";
                    if (ofd.ShowDialog(this) == DialogResult.OK)
                    {
                        txtPath.Text = ofd.FileName;
                    }
                }
            };

            Label lblArgs = new Label();
            lblArgs.Text = "起動引数 ({file}、{0}、または %1 がファイルパスに置換されます):";
            lblArgs.Location = new Point(20, 85);
            lblArgs.AutoSize = true;

            txtArgs = new TextBox();
            txtArgs.Location = new Point(20, 110);
            txtArgs.Size = new Size(430, 26);
            txtArgs.Text = config.EditorArguments ?? "\"{file}\"";

            Button btnTest = new Button();
            btnTest.Text = "テスト起動";
            btnTest.Location = new Point(460, 108);
            btnTest.Size = new Size(85, 30);
            btnTest.Click += delegate
            {
                string path = txtPath.Text.Trim();
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    Program.ShowWarning("指定されたエディター実行ファイルが見つかりません。");
                    return;
                }

                try
                {
                    string testFile = Path.Combine(Program.WorkDirectory, "test.txt");
                    File.WriteAllText(testFile, "テスト起動用のファイルです。", Encoding.UTF8);

                    string argsTemplate = txtArgs.Text.Trim();
                    string args;
                    if (argsTemplate.Contains("{file}")) args = argsTemplate.Replace("{file}", testFile);
                    else if (argsTemplate.Contains("{0}")) args = string.Format(argsTemplate, testFile);
                    else if (argsTemplate.Contains("%1")) args = argsTemplate.Replace("%1", "\"" + testFile + "\"");
                    else if (string.IsNullOrEmpty(argsTemplate)) args = "\"" + testFile + "\"";
                    else args = argsTemplate + " \"" + testFile + "\"";

                    Process.Start(path, args);
                }
                catch (Exception ex)
                {
                    Program.ShowErrorMessage("起動テスト中にエラーが発生しました:\r\n" + ex.Message);
                }
            };

            Button btnSave = new Button();
            btnSave.Text = "保存";
            btnSave.Location = new Point(360, 175);
            btnSave.Size = new Size(90, 34);
            btnSave.Click += delegate
            {
                string path = txtPath.Text.Trim();
                if (string.IsNullOrEmpty(path))
                {
                    Program.ShowWarning("エディター実行ファイルのパスを入力してください。");
                    return;
                }

                if (!File.Exists(path))
                {
                    if (!Program.ConfirmOperation("指定されたファイルが見つかりません。このまま保存しますか？"))
                    {
                        return;
                    }
                }

                AppConfig newConfig = new AppConfig();
                newConfig.EditorPath = path;
                newConfig.EditorArguments = txtArgs.Text.Trim();
                Program.SaveApplicationConfig(newConfig);

                this.DialogResult = DialogResult.OK;
                this.Close();
            };

            Button btnCancel = new Button();
            btnCancel.Text = "キャンセル";
            btnCancel.DialogResult = DialogResult.Cancel;
            btnCancel.Location = new Point(460, 175);
            btnCancel.Size = new Size(85, 34);

            this.Controls.Add(lblPath);
            this.Controls.Add(txtPath);
            this.Controls.Add(btnBrowse);
            this.Controls.Add(lblArgs);
            this.Controls.Add(txtArgs);
            this.Controls.Add(btnTest);
            this.Controls.Add(btnSave);
            this.Controls.Add(btnCancel);

            this.AcceptButton = btnSave;
            this.CancelButton = btnCancel;
        }
    }

    public class UndoForm : Form
    {
        private ListBox listBox;
        private TextBox detailTextBox;
        private Button undoButton;
        private List<HistorySession> currentSessions;

        public UndoForm()
        {
            InitializeComponent();
            PopulateList();
        }

        private void InitializeComponent()
        {
            this.Text = Program.AppName + " - Undo履歴";
            this.Size = new Size(780, 530);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);

            Label instructionLabel = new Label();
            instructionLabel.Text = "Undoする履歴を選択してください:";
            instructionLabel.Location = new Point(15, 12);
            instructionLabel.AutoSize = true;

            listBox = new ListBox();
            listBox.Location = new Point(15, 35);
            listBox.Size = new Size(735, 150);
            listBox.Font = new Font("Lucida Console", 9.5f);
            listBox.SelectedIndexChanged += delegate { UpdateDetail(); };

            Label detailLabel = new Label();
            detailLabel.Text = "履歴詳細:";
            detailLabel.Location = new Point(15, 195);
            detailLabel.AutoSize = true;

            detailTextBox = new TextBox();
            detailTextBox.Location = new Point(15, 218);
            detailTextBox.Size = new Size(735, 200);
            detailTextBox.Multiline = true;
            detailTextBox.ReadOnly = true;
            detailTextBox.ScrollBars = ScrollBars.Vertical;
            detailTextBox.Font = new Font("Lucida Console", 9f);

            undoButton = new Button();
            undoButton.Text = "選択履歴を元に戻す";
            undoButton.Location = new Point(480, 432);
            undoButton.Size = new Size(165, 36);
            undoButton.Click += delegate { ExecuteUndo(); };

            Button closeButton = new Button();
            closeButton.Text = "閉じる";
            closeButton.DialogResult = DialogResult.Cancel;
            closeButton.Location = new Point(655, 432);
            closeButton.Size = new Size(95, 36);
            closeButton.Click += delegate { this.Close(); };

            this.Controls.Add(instructionLabel);
            this.Controls.Add(listBox);
            this.Controls.Add(detailLabel);
            this.Controls.Add(detailTextBox);
            this.Controls.Add(undoButton);
            this.Controls.Add(closeButton);

            this.AcceptButton = undoButton;
            this.CancelButton = closeButton;
        }

        private void PopulateList()
        {
            listBox.Items.Clear();
            currentSessions = Program.GetRenameHistorySessions();

            foreach (HistorySession session in currentSessions)
            {
                DateTime createdDate;
                if (!DateTime.TryParse(session.Created, out createdDate))
                {
                    createdDate = DateTime.Now;
                }

                string dateString = createdDate.ToString("yyyy-MM-dd HH:mm:ss");
                int itemCount = session.Entries != null ? session.Entries.Count : 0;
                string modeString = string.Format("[{0}]", session.Mode);
                string undoneFlag = session.Undone ? "  (Undo済み)" : "";

                string lineText = string.Format("{0}   {1,5} 件   {2,-10}{3}", dateString, itemCount, modeString, undoneFlag);
                listBox.Items.Add(lineText);
            }

            if (listBox.Items.Count > 0)
            {
                listBox.SelectedIndex = 0;
            }
            else
            {
                undoButton.Enabled = false;
            }
        }

        private void UpdateDetail()
        {
            int index = listBox.SelectedIndex;
            if (index < 0 || index >= currentSessions.Count)
            {
                detailTextBox.Text = string.Empty;
                undoButton.Enabled = false;
                return;
            }

            HistorySession session = currentSessions[index];
            List<string> lines = new List<string>();

            lines.Add(string.Format("セッションID: {0}", session.SessionId));
            lines.Add(string.Format("実行日時: {0}", session.Created));
            lines.Add(string.Format("処理種別: {0}", session.Mode));
            lines.Add(string.Format("対象件数: {0} 件", session.Entries != null ? session.Entries.Count : 0));

            string undoStatus;
            if (session.Undone)
            {
                if (!string.IsNullOrEmpty(session.UndoneAt))
                {
                    undoStatus = string.Format("Undo済み ({0})", session.UndoneAt);
                }
                else
                {
                    undoStatus = "Undo済み";
                }
            }
            else
            {
                undoStatus = "未実行";
            }
            lines.Add(string.Format("Undo状態: {0}", undoStatus));
            lines.Add("------------------------------------------------------------");

            if (session.Entries != null)
            {
                int displayMax = Math.Min(30, session.Entries.Count);
                for (int i = 0; i < displayMax; i++)
                {
                    HistoryEntry entry = session.Entries[i];
                    lines.Add(string.Format("[{0}]", entry.Id));
                    string src = !string.IsNullOrEmpty(entry.SourcePath) ? entry.SourcePath : entry.Original;
                    string dst = !string.IsNullOrEmpty(entry.DestinationPath) ? entry.DestinationPath : entry.Current;
                    lines.Add(string.Format("  変更前: {0}", src));
                    lines.Add(string.Format("  変更後: {0}", dst));
                }

                if (session.Entries.Count > displayMax)
                {
                    lines.Add(string.Format("ほか {0} 件...", session.Entries.Count - displayMax));
                }
            }

            detailTextBox.Text = string.Join("\r\n", lines.ToArray());
            undoButton.Enabled = !session.Undone;
        }

        private void ExecuteUndo()
        {
            int index = listBox.SelectedIndex;
            if (index < 0 || index >= currentSessions.Count)
            {
                Program.ShowWarning("元に戻す履歴を選択してください。");
                return;
            }

            HistorySession session = currentSessions[index];

            if (session.Undone)
            {
                if (!Program.ConfirmOperation("選択した履歴は既にUndoされています。\r\n再度元に戻す処理を実行しますか？", Program.AppName + " - Undoの確認"))
                {
                    return;
                }
            }

            List<RenameItem> undoItems = new List<RenameItem>();
            foreach (HistoryEntry entry in session.Entries)
            {
                string undoSource = !string.IsNullOrEmpty(entry.DestinationPath) ? entry.DestinationPath : entry.Current;
                string undoDest = !string.IsNullOrEmpty(entry.SourcePath) ? entry.SourcePath : entry.Original;

                RenameItem item = new RenameItem();
                item.Id = entry.Id;
                item.SourcePath = undoSource;
                item.DestinationPath = undoDest;
                undoItems.Add(item);
            }

            if (!Program.ShowOperationPreview(undoItems, "Undo実行予定", this))
            {
                return;
            }

            BatchResult batchResult = Program.InvokeRenameMoveBatch(undoItems);

            string undoHistoryFile = null;
            if (batchResult.Success.Count > 0)
            {
                undoHistoryFile = Program.SaveRenameHistory(batchResult.Success, "Undo", session.SessionId);
            }

            string summaryMessage = string.Format(
                "Undo処理が完了しました。\r\n\r\n成功: {0} 件\r\n失敗: {1} 件",
                batchResult.Success.Count,
                batchResult.Failed.Count
            );

            if (batchResult.Failed.Count == 0)
            {
                Program.SetSessionUndone(session);
                Program.ShowInfo(summaryMessage);
            }
            else
            {
                Program.ShowWarning(summaryMessage);

                string newSessionId = !string.IsNullOrEmpty(undoHistoryFile)
                    ? Path.GetFileNameWithoutExtension(undoHistoryFile)
                    : string.Empty;

                RetryResult retryResult = Program.EditAndRetryFailedItems(
                    batchResult.Failed,
                    "Undo失敗項目の再試行",
                    "UndoRetry",
                    newSessionId,
                    this
                );

                if (retryResult != null && retryResult.RemainingFailedItems.Count == 0)
                {
                    Program.SetSessionUndone(session);
                }
            }

            PopulateList();
        }
    }
}
