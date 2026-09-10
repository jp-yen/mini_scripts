using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace GpgUi.Views
{
    public partial class CryptoView : UserControl
    {
        private readonly GpgService _gpgService;

        private TabControl _tabControl;

        // ファイル・フォルダ暗号化用コントロール
        private TextBlock _lblEncryptSelectedPath;
        private List<string> _lastEncryptSourcePaths = new List<string>();
        private ComboBox _cboSignKeyFile;
        private ComboBox _cboOutputFormatFile;
        private PasswordBox _txtSignPassphraseFile;
        private TextBox _txtRecipientSearchFile;
        private ListView _lstRecipientsFile;
        private TextBlock _lblRecipientCountFile;
        private PasswordBox _txtSymmetricPassphraseFile;

        // テキスト暗号化用コントロール
        private TextBox _txtPlaintextInput;
        private TextBox _txtEncryptOutput;
        private ComboBox _cboSignKeyText;
        private PasswordBox _txtSignPassphraseText;
        private TextBox _txtRecipientSearchText;
        private ListView _lstRecipientsText;
        private TextBlock _lblRecipientCountText;
        private PasswordBox _txtSymmetricPassphraseText;

        // 鍵データ・選択状態（両方のタブで共通同期）
        private List<GpgKey> _allKeys;
        private HashSet<string> _selectedRecipientFingerprints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private bool _isUpdatingSelection = false;

        // 復号化用コントロール
        private TextBox _txtDecryptInput;
        private TextBox _txtDecryptOutput;
        private PasswordBox _txtDecryptPassphrase;
        private Border _signerInfoCard;
        private TextBlock _lblSignerInfo;
        private Button _btnSaveToSameFolder;
        private string _lastSourceFilePath;

        public CryptoView(GpgService gpgService)
        {
            _allKeys = new List<GpgKey>();
            _gpgService = gpgService;

            InitializeUI();
            Loaded += async (s, e) => await RefreshKeysAsync();
        }

        private void InitializeUI()
        {
            var mainGrid = new Grid { Margin = new Thickness(16) };

            _tabControl = new TabControl
            {
                Background = ThemeHelper.BrushBgDark,
                BorderBrush = ThemeHelper.BrushBorder,
                Foreground = ThemeHelper.BrushTextMain
            };

            var tabEncryptFile = new TabItem { Header = "暗号化 (ファイル・フォルダ)" };
            tabEncryptFile.Content = CreateEncryptFilePanel();

            var tabEncryptText = new TabItem { Header = "暗号化 (テキスト)" };
            tabEncryptText.Content = CreateEncryptTextPanel();

            var tabDecrypt = new TabItem { Header = "復号化" };
            tabDecrypt.Content = CreateDecryptPanel();

            _tabControl.Items.Add(tabEncryptFile);
            _tabControl.Items.Add(tabEncryptText);
            _tabControl.Items.Add(tabDecrypt);

            mainGrid.Children.Add(_tabControl);
            Content = mainGrid;
        }

        private bool _isRefreshing = false;

        public async Task RefreshKeysAsync()
        {
            if (_isRefreshing) return;
            _isRefreshing = true;

            try
            {
                _allKeys = await _gpgService.GetKeysAsync();

                PopulateSignerKeys(_cboSignKeyFile);
                PopulateSignerKeys(_cboSignKeyText);

                UpdateRecipientList(_lstRecipientsFile, _txtRecipientSearchFile != null ? _txtRecipientSearchFile.Text : "");
                UpdateRecipientList(_lstRecipientsText, _txtRecipientSearchText != null ? _txtRecipientSearchText.Text : "");
            }
            finally
            {
                _isRefreshing = false;
            }
        }

        private void PopulateSignerKeys(ComboBox cbo)
        {
            if (cbo == null) return;
            cbo.Items.Clear();
            cbo.Items.Add("-- 署名なし --");

            var secretKeys = _allKeys.Where(k => k.IsSecretKey).ToList();
            foreach (var k in secretKeys)
            {
                cbo.Items.Add(k.DisplayTitle);
            }
            cbo.SelectedIndex = 0;
        }

        private void UpdateRecipientList(ListView listView, string filterQuery)
        {
            if (listView == null || _allKeys == null) return;

            string query = (filterQuery ?? "").Trim().ToLower();

            var filtered = _allKeys.Where(k =>
            {
                if (!k.IsUsableForEncryption) return false;
                if (string.IsNullOrEmpty(query)) return true;
                return (k.DisplayTitle ?? "").ToLower().Contains(query) ||
                       (k.Fingerprint ?? "").ToLower().Contains(query);
            }).ToList();

            listView.ItemsSource = filtered;

            _isUpdatingSelection = true;
            try
            {
                listView.SelectedItems.Clear();
                foreach (var item in filtered)
                {
                    if (!string.IsNullOrEmpty(item.Fingerprint) && _selectedRecipientFingerprints.Contains(item.Fingerprint))
                    {
                        listView.SelectedItems.Add(item);
                    }
                }
            }
            finally
            {
                _isUpdatingSelection = false;
            }

            UpdateRecipientCountDisplay();
        }

        private void UpdateRecipientCountDisplay()
        {
            int count = _selectedRecipientFingerprints.Count;
            string txt = string.Format("(選択: {0} 件)", count);

            if (_lblRecipientCountFile != null) _lblRecipientCountFile.Text = txt;
            if (_lblRecipientCountText != null) _lblRecipientCountText.Text = txt;
        }

        private List<string> GetSelectedRecipientFingerprints()
        {
            return _selectedRecipientFingerprints.ToList();
        }

        public async Task OpenFileSmartAsync(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return;
            await OpenFilesSmartAsync(new[] { filePath });
        }

        public async Task OpenFilesSmartAsync(string[] filePaths)
        {
            if (filePaths == null || filePaths.Length == 0) return;

            var validPaths = filePaths.Where(p => !string.IsNullOrEmpty(p) && (File.Exists(p) || Directory.Exists(p))).ToList();
            if (validPaths.Count == 0) return;

            // 単一ファイルで暗号化形式（.asc, .gpg, .pgp）の場合は復号化タブへ誘導
            if (validPaths.Count == 1 && File.Exists(validPaths[0]))
            {
                string singleFile = validPaths[0];
                string ext = Path.GetExtension(singleFile).ToLower();
                if (ext == ".asc" || ext == ".gpg" || ext == ".pgp")
                {
                    _tabControl.SelectedIndex = 2; // 復号化タブへ移動
                    _txtDecryptInput.Text = singleFile;
                    string pass = _txtDecryptPassphrase != null ? _txtDecryptPassphrase.Password : "";
                    await DecryptAndSaveFileAsync(singleFile, pass);
                    return;
                }
            }

            // 暗号化対象として設定
            _lastEncryptSourcePaths = validPaths;

            if (validPaths.Count == 1)
            {
                string single = validPaths[0];
                if (Directory.Exists(single))
                {
                    _lblEncryptSelectedPath.Text = string.Format("選択中の対象: {0} (フォルダー - ZIPアーカイブ化)", single);
                }
                else
                {
                    _lblEncryptSelectedPath.Text = string.Format("選択中の対象: {0}", single);
                }
            }
            else
            {
                int fileCount = validPaths.Count(File.Exists);
                int dirCount = validPaths.Count(Directory.Exists);
                string detail = string.Format("{0} 件のアイテム (ファイル {1}件, フォルダー {2}件) - 自動的に1つのZIPにまとめて暗号化", validPaths.Count, fileCount, dirCount);
                _lblEncryptSelectedPath.Text = string.Format("選択中の対象: {0}", detail);
            }

            _tabControl.SelectedIndex = 0; // ファイル暗号化タブへ移動
        }

        private async Task HandleEncryptDropAsync(DragEventArgs e)
        {
            e.Handled = true;
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files != null && files.Length > 0)
            {
                await OpenFilesSmartAsync(files);
            }
        }

        private async Task HandleDecryptDropAsync(DragEventArgs e)
        {
            e.Handled = true;
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            string filePath = GetFirstDroppedPath(e);
            if (filePath != null && File.Exists(filePath))
            {
                _tabControl.SelectedIndex = 2; // 復号化タブ
                _txtDecryptInput.Text = filePath;
                string pass = _txtDecryptPassphrase != null ? _txtDecryptPassphrase.Password : "";
                await DecryptAndSaveFileAsync(filePath, pass);
            }
        }

        private static string GetFirstDroppedPath(DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return null;
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
            return (files != null && files.Length > 0) ? files[0] : null;
        }

        private void CopyText(string text)
        {
            ThemeHelper.CopyTextToClipboard(text);
        }

        private void SaveTextToFile(string text, string defaultName)
        {
            ThemeHelper.SaveTextToFile(text, defaultName);
        }

        private string GetSelectedSignKeyFingerprint(ComboBox cbo)
        {
            if (cbo == null || cbo.SelectedIndex <= 0) return null;
            var secretKeys = _allKeys.Where(k => k.IsSecretKey).ToList();
            int idx = cbo.SelectedIndex - 1;
            return (idx < secretKeys.Count) ? secretKeys[idx].Fingerprint : null;
        }
    }
}
