using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GpgUi.Views
{
    public class SignVerifyView : UserControl
    {
        private readonly GpgService _gpgService;

        private TabControl _tabControl;

        private ComboBox _cboSignKey;
        private ComboBox _cboSignMode;
        private PasswordBox _txtPassphrase;
        private TextBox _txtSignInput;
        private TextBox _txtSignOutput;
        private List<GpgKey> _allKeys;

        private TextBox _txtVerifyInput;
        private TextBox _txtSignatureInput;
        private Border _statusBannerCard;
        private TextBlock _lblStatusBanner;
        private TextBox _txtVerifiedContent;

        public SignVerifyView(GpgService gpgService)
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

            var tabSign = new TabItem { Header = "署名" };
            tabSign.Content = CreateSignPanel();

            var tabVerify = new TabItem { Header = "検証" };
            tabVerify.Content = CreateVerifyPanel();

            _tabControl.Items.Add(tabSign);
            _tabControl.Items.Add(tabVerify);

            mainGrid.Children.Add(_tabControl);
            Content = mainGrid;
        }

        private UIElement CreateSignPanel()
        {
            var grid = new Grid { Margin = new Thickness(12) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var settingsPnl = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };

            settingsPnl.Children.Add(new TextBlock { Text = "署名用の秘密鍵:", Foreground = ThemeHelper.BrushTextMuted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 8) });
            _cboSignKey = new ComboBox
            {
                Height = 36,
                Width = 240,
                Background = ThemeHelper.BrushBgDark,
                Foreground = ThemeHelper.BrushTextMain,
                BorderBrush = ThemeHelper.BrushBorder,
                VerticalContentAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 16, 8)
            };
            settingsPnl.Children.Add(_cboSignKey);

            settingsPnl.Children.Add(new TextBlock { Text = "署名モード:", Foreground = ThemeHelper.BrushTextMuted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 8) });
            _cboSignMode = new ComboBox
            {
                Height = 36,
                Width = 180,
                Background = ThemeHelper.BrushBgDark,
                Foreground = ThemeHelper.BrushTextMain,
                BorderBrush = ThemeHelper.BrushBorder,
                VerticalContentAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 16, 8)
            };
            _cboSignMode.Items.Add("クリアテキスト (--clearsign)");
            _cboSignMode.Items.Add("分離署名 (--detach-sign)");
            _cboSignMode.Items.Add("ASCII 署名 (--sign --armor)");
            _cboSignMode.SelectedIndex = 0;
            settingsPnl.Children.Add(_cboSignMode);

            settingsPnl.Children.Add(new TextBlock { Text = "パスフレーズ:", Foreground = ThemeHelper.BrushTextMuted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 8) });
            _txtPassphrase = ThemeHelper.CreateStyledPasswordBox();
            _txtPassphrase.Width = 160;
            _txtPassphrase.Margin = new Thickness(0, 0, 16, 8);
            settingsPnl.Children.Add(_txtPassphrase);

            var settingsCard = ThemeHelper.CreateCardPanel(settingsPnl, 10);
            Grid.SetRow(settingsCard, 0);
            grid.Children.Add(settingsCard);

            _txtSignInput = ThemeHelper.CreateStyledTextBox();
            _txtSignInput.MinHeight = 120;
            _txtSignInput.AcceptsReturn = true;
            _txtSignInput.TextWrapping = TextWrapping.Wrap;
            _txtSignInput.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            _txtSignInput.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            _txtSignInput.FontFamily = new FontFamily("Consolas");
            _txtSignInput.VerticalContentAlignment = VerticalAlignment.Top;
            _txtSignInput.Padding = new Thickness(6, 6, 6, 6);
            _txtSignInput.AllowDrop = true;
            _txtSignInput.PreviewDragOver += (s, e) => { e.Effects = DragDropEffects.Copy; e.Handled = true; };
            _txtSignInput.Drop += (s, e) => HandleFileDropToTextBox(e, _txtSignInput);

            var inHeaderStack = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            inHeaderStack.Children.Add(new TextBlock { Text = "署名対象のメッセージまたはファイルパス:", Foreground = ThemeHelper.BrushTextMuted, FontSize = 13, VerticalAlignment = VerticalAlignment.Center });
            var btnBrowseSign = ThemeHelper.CreateSecondaryButton("ファイル選択", (s, e) => BrowseSignFile());
            btnBrowseSign.Height = 28;
            btnBrowseSign.Margin = new Thickness(12, 0, 0, 0);
            inHeaderStack.Children.Add(btnBrowseSign);

            var btnClearSignInput = ThemeHelper.CreateSecondaryButton("🗑 クリア", (s, e) => _txtSignInput.Text = "");
            btnClearSignInput.Height = 28;
            btnClearSignInput.Margin = new Thickness(6, 0, 0, 0);
            inHeaderStack.Children.Add(btnClearSignInput);

            var inPnl = new StackPanel();
            inPnl.Children.Add(inHeaderStack);
            inPnl.Children.Add(_txtSignInput);

            var inputCard = ThemeHelper.CreateCardPanel(inPnl, 12);
            Grid.SetRow(inputCard, 1);
            grid.Children.Add(inputCard);

            var actBar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 6, 6, 6) };
            actBar.Children.Add(ThemeHelper.CreatePrimaryButton("デジタル署名を作成", async (s, e) => await OnSignClickAsync()));
            Grid.SetRow(actBar, 2);
            grid.Children.Add(actBar);

            _txtSignOutput = ThemeHelper.CreateStyledTextBox("", true);
            _txtSignOutput.MinHeight = 120;
            _txtSignOutput.AcceptsReturn = true;
            _txtSignOutput.TextWrapping = TextWrapping.Wrap;
            _txtSignOutput.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            _txtSignOutput.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            _txtSignOutput.FontFamily = new FontFamily("Consolas");
            _txtSignOutput.VerticalContentAlignment = VerticalAlignment.Top;
            _txtSignOutput.Padding = new Thickness(6, 6, 6, 6);

            var outBtnStack = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            outBtnStack.Children.Add(ThemeHelper.CreateSecondaryButton("署名付きテキストをコピー", (s, e) => CopyText(_txtSignOutput.Text)));
            outBtnStack.Children.Add(ThemeHelper.CreateSecondaryButton("ファイルに保存...", (s, e) => SaveTextToFile(_txtSignOutput.Text, "signed_document.asc")));

            var outPnl = new StackPanel();
            outPnl.Children.Add(new TextBlock { Text = "署名付き出力結果:", Foreground = ThemeHelper.BrushTextMuted, FontSize = 13, Margin = new Thickness(0, 0, 0, 6) });
            outPnl.Children.Add(_txtSignOutput);
            outPnl.Children.Add(outBtnStack);

            var outputCard = ThemeHelper.CreateCardPanel(outPnl, 12);
            Grid.SetRow(outputCard, 3);
            grid.Children.Add(outputCard);

            return new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = grid
            };
        }

        private UIElement CreateVerifyPanel()
        {
            var grid = new Grid { Margin = new Thickness(12) };
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            _txtVerifyInput = ThemeHelper.CreateStyledTextBox();
            _txtVerifyInput.MinHeight = 130;
            _txtVerifyInput.AcceptsReturn = true;
            _txtVerifyInput.TextWrapping = TextWrapping.Wrap;
            _txtVerifyInput.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            _txtVerifyInput.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            _txtVerifyInput.FontFamily = new FontFamily("Consolas");
            _txtVerifyInput.VerticalContentAlignment = VerticalAlignment.Top;
            _txtVerifyInput.Padding = new Thickness(6, 6, 6, 6);
            _txtVerifyInput.AllowDrop = true;
            _txtVerifyInput.PreviewDragOver += (s, e) => { e.Effects = DragDropEffects.Copy; e.Handled = true; };
            _txtVerifyInput.Drop += async (s, e) =>
            {
                HandleFileDropToTextBox(e, _txtVerifyInput);
                await OnVerifyClickAsync();
            };

            var inHeaderStack = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            inHeaderStack.Children.Add(new TextBlock { Text = "検証する署名付きメッセージまたはファイル (ファイルドロップ可):", Foreground = ThemeHelper.BrushTextMuted, FontSize = 13, VerticalAlignment = VerticalAlignment.Center });
            var btnBrowseVerify = ThemeHelper.CreateSecondaryButton("ファイルを選択...", (s, e) => BrowseVerifyFile());
            btnBrowseVerify.Height = 28;
            btnBrowseVerify.Margin = new Thickness(12, 0, 0, 0);
            inHeaderStack.Children.Add(btnBrowseVerify);

            var btnClearVerifyInput = ThemeHelper.CreateSecondaryButton("🗑 クリア", (s, e) => {
                _txtVerifyInput.Text = "";
                _txtVerifiedContent.Text = "";
                if (_statusBannerCard != null) _statusBannerCard.Visibility = Visibility.Collapsed;
            });
            btnClearVerifyInput.Height = 28;
            btnClearVerifyInput.Margin = new Thickness(6, 0, 0, 0);
            inHeaderStack.Children.Add(btnClearVerifyInput);

            var inPnl = new StackPanel();
            inPnl.Children.Add(inHeaderStack);
            inPnl.Children.Add(_txtVerifyInput);

            var inputCard = ThemeHelper.CreateCardPanel(inPnl, 12);
            Grid.SetRow(inputCard, 0);
            grid.Children.Add(inputCard);

            _txtSignatureInput = ThemeHelper.CreateStyledTextBox();
            _txtSignatureInput.MinHeight = 60;
            _txtSignatureInput.MaxHeight = 160;
            _txtSignatureInput.AcceptsReturn = true;
            _txtSignatureInput.TextWrapping = TextWrapping.Wrap;
            _txtSignatureInput.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            _txtSignatureInput.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            _txtSignatureInput.FontFamily = new FontFamily("Consolas");
            _txtSignatureInput.VerticalContentAlignment = VerticalAlignment.Top;
            _txtSignatureInput.Padding = new Thickness(6, 6, 6, 6);
            _txtSignatureInput.AllowDrop = true;
            _txtSignatureInput.PreviewDragOver += (s, e) => { e.Effects = DragDropEffects.Copy; e.Handled = true; };
            _txtSignatureInput.Drop += async (s, e) =>
            {
                HandleFileDropToTextBox(e, _txtSignatureInput);
                await OnVerifyClickAsync();
            };

            var sigHeaderStack = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            sigHeaderStack.Children.Add(new TextBlock { Text = "分離署名データ / ファイル (.sig / .asc) (分離署名の検証時のみ指定):", Foreground = ThemeHelper.BrushTextMuted, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
            var btnBrowseSig = ThemeHelper.CreateSecondaryButton("署名ファイルを選択...", (s, e) => BrowseSignatureFile());
            btnBrowseSig.Height = 26;
            btnBrowseSig.Margin = new Thickness(12, 0, 0, 0);
            sigHeaderStack.Children.Add(btnBrowseSig);

            var btnClearSigInput = ThemeHelper.CreateSecondaryButton("🗑 クリア", (s, e) => _txtSignatureInput.Text = "");
            btnClearSigInput.Height = 26;
            btnClearSigInput.Margin = new Thickness(6, 0, 0, 0);
            sigHeaderStack.Children.Add(btnClearSigInput);

            var sigPnl = new StackPanel();
            sigPnl.Children.Add(sigHeaderStack);
            sigPnl.Children.Add(_txtSignatureInput);

            var sigCard = ThemeHelper.CreateCardPanel(sigPnl, 10);
            Grid.SetRow(sigCard, 1);
            grid.Children.Add(sigCard);

            var actBar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 6, 6, 6) };
            actBar.Children.Add(ThemeHelper.CreatePrimaryButton("署名を検証", async (s, e) => await OnVerifyClickAsync()));
            Grid.SetRow(actBar, 2);
            grid.Children.Add(actBar);

            _lblStatusBanner = new TextBlock { FontSize = 14, FontWeight = FontWeights.Bold };
            _statusBannerCard = ThemeHelper.CreateCardPanel(_lblStatusBanner, 14);
            _statusBannerCard.Visibility = Visibility.Collapsed;
            _statusBannerCard.Margin = new Thickness(0, 0, 0, 10);
            Grid.SetRow(_statusBannerCard, 3);
            grid.Children.Add(_statusBannerCard);

            _txtVerifiedContent = ThemeHelper.CreateStyledTextBox("", true);
            _txtVerifiedContent.MinHeight = 120;
            _txtVerifiedContent.AcceptsReturn = true;
            _txtVerifiedContent.TextWrapping = TextWrapping.Wrap;
            _txtVerifiedContent.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            _txtVerifiedContent.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            _txtVerifiedContent.FontFamily = new FontFamily("Consolas");
            _txtVerifiedContent.VerticalContentAlignment = VerticalAlignment.Top;
            _txtVerifiedContent.Padding = new Thickness(6, 6, 6, 6);

            var verBtnStack = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            verBtnStack.Children.Add(ThemeHelper.CreateSecondaryButton("検証本文をコピー", (s, e) => CopyText(_txtVerifiedContent.Text)));
            verBtnStack.Children.Add(ThemeHelper.CreateSecondaryButton("テキストファイルに保存...", (s, e) => SaveTextToFile(_txtVerifiedContent.Text, "verified_document.txt")));

            var verPnl = new StackPanel();
            verPnl.Children.Add(new TextBlock { Text = "検証された本文 / 詳細情報:", Foreground = ThemeHelper.BrushTextMuted, FontSize = 12, Margin = new Thickness(0, 0, 0, 6) });
            verPnl.Children.Add(_txtVerifiedContent);
            verPnl.Children.Add(verBtnStack);

            var verifiedCard = ThemeHelper.CreateCardPanel(verPnl, 12);
            Grid.SetRow(verifiedCard, 4);
            grid.Children.Add(verifiedCard);

            return new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = grid
            };
        }

        private bool _isRefreshing = false;

        public async Task RefreshKeysAsync()
        {
            if (_isRefreshing) return;
            _isRefreshing = true;

            try
            {
                _allKeys = await _gpgService.GetKeysAsync();

                _cboSignKey.Items.Clear();
                var secretKeys = _allKeys.Where(k => k.IsSecretKey).ToList();
                foreach (var k in secretKeys)
                {
                    _cboSignKey.Items.Add(k.DisplayTitle);
                }
                if (_cboSignKey.Items.Count > 0) _cboSignKey.SelectedIndex = 0;
            }
            finally
            {
                _isRefreshing = false;
            }
        }

        private void BrowseSignFile()
        {
            var ofd = new Microsoft.Win32.OpenFileDialog { Title = "署名するファイルを選択 (すべての形式に対応)" };
            if (ofd.ShowDialog() == true)
            {
                _txtSignInput.Text = ofd.FileName;
            }
        }

        private void BrowseVerifyFile()
        {
            var ofd = new Microsoft.Win32.OpenFileDialog { Title = "検証対象のファイルを選択" };
            if (ofd.ShowDialog() == true)
            {
                _txtVerifyInput.Text = ofd.FileName;
            }
        }

        private void BrowseSignatureFile()
        {
            var ofd = new Microsoft.Win32.OpenFileDialog
            {
                Title = "分離署名ファイル (.sig, .asc) を選択",
                Filter = "署名ファイル (*.sig;*.asc)|*.sig;*.asc|すべてのファイル (*.*)|*.*"
            };
            if (ofd.ShowDialog() == true)
            {
                _txtSignatureInput.Text = ofd.FileName;
            }
        }

        private async Task OnSignClickAsync()
        {
            string text = _txtSignInput.Text.Trim();
            if (string.IsNullOrEmpty(text))
            {
                MessageBox.Show("署名するテキストまたはファイルパスを入力してください。", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var secretKeys = _allKeys.Where(k => k.IsSecretKey).ToList();
            if (secretKeys.Count == 0 || _cboSignKey.SelectedIndex < 0 || _cboSignKey.SelectedIndex >= secretKeys.Count)
            {
                MessageBox.Show("署名に使用する秘密鍵を選択してください。", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string fpr = secretKeys[_cboSignKey.SelectedIndex].Fingerprint;
            string mode = "Clearsign";
            if (_cboSignMode.SelectedIndex == 1) mode = "Detached";
            else if (_cboSignMode.SelectedIndex == 2) mode = "Ascii";
            string pass = _txtPassphrase.Password;

            if (File.Exists(text) || Directory.Exists(text))
            {
                string defaultExt = ".asc";
                if (mode == "Detached") defaultExt = ".sig";
                else if (mode == "Ascii") defaultExt = ".asc";

                string defaultName = Path.GetFileName(text) + defaultExt;
                string initDir = Path.GetDirectoryName(text);

                var sfd = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "署名ファイルの保存先を選択",
                    InitialDirectory = initDir,
                    FileName = defaultName,
                    Filter = "署名ファイル (*.asc;*.sig;*.gpg)|*.asc;*.sig;*.gpg|すべてのファイル (*.*)|*.*"
                };

                if (sfd.ShowDialog() != true) return;

                var fileRes = await _gpgService.SignFileAsync(text, sfd.FileName, fpr, mode, pass);
                if (fileRes.Success)
                {
                    _txtSignOutput.Text = string.Format("[ファイルの署名完了]\n保存先: {0}", sfd.FileName);
                    MessageBox.Show("ファイルを正常に署名して保存しました:\n" + sfd.FileName, "署名成功", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("ファイルの署名に失敗しました:\n" + fileRes.ErrorMessage, "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                CleanupINetCacheFile(text);
                return;
            }

            var res = await _gpgService.SignTextAsync(text, fpr, mode, pass);
            if (res.Success)
            {
                _txtSignOutput.Text = res.OutputText;
            }
            else
            {
                MessageBox.Show("署名の作成に失敗しました:\n" + res.ErrorMessage, "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task OnVerifyClickAsync()
        {
            string text = _txtVerifyInput.Text.Trim();
            string sigText = _txtSignatureInput.Text.Trim();

            if (string.IsNullOrEmpty(text) && string.IsNullOrEmpty(sigText))
            {
                MessageBox.Show("検証する署名付き文書、ファイル、または署名データを入力してください。", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SignVerifyResult res;

            bool isFileText = File.Exists(text);
            bool isFileSig = File.Exists(sigText);

            if (isFileText || isFileSig)
            {
                string targetFile = isFileText ? text : null;
                string sigFile = isFileSig ? sigText : null;
                res = await _gpgService.VerifyFileAsync(targetFile, sigFile);
                CleanupINetCacheFile(text);
                CleanupINetCacheFile(sigText);
            }
            else
            {
                res = await _gpgService.VerifyTextAsync(text, string.IsNullOrEmpty(sigText) ? null : sigText);
            }

            _statusBannerCard.Visibility = Visibility.Visible;
            if (res.IsValid)
            {
                _statusBannerCard.Background = new SolidColorBrush(Color.FromArgb(50, 16, 185, 129));
                _statusBannerCard.BorderBrush = ThemeHelper.BrushSuccessGreen;
                _lblStatusBanner.Foreground = ThemeHelper.BrushSuccessGreen;
                _lblStatusBanner.Text = string.Format("✓ 正しい署名です (Good Signature)\n署名者: {0}\nキーID / フィンガープリント: {1}", res.SignerUid, res.SignerFingerprint);
                _txtVerifiedContent.Text = string.IsNullOrEmpty(res.VerifiedText) ? "[ファイル署名の検証完了]" : res.VerifiedText;
            }
            else
            {
                _statusBannerCard.Background = new SolidColorBrush(Color.FromArgb(50, 239, 68, 68));
                _statusBannerCard.BorderBrush = ThemeHelper.BrushDangerRed;
                _lblStatusBanner.Foreground = ThemeHelper.BrushDangerRed;
                _lblStatusBanner.Text = "✗ 署名検証に失敗しました\n" + res.StatusMessage;
                _txtVerifiedContent.Text = res.StatusMessage;
            }
        }

        private void CopyText(string text)
        {
            ThemeHelper.CopyTextToClipboard(text);
        }

        private void SaveTextToFile(string text, string defaultName)
        {
            ThemeHelper.SaveTextToFile(text, defaultName);
        }

        private void HandleFileDropToTextBox(DragEventArgs e, TextBox targetBox)
        {
            e.Handled = true;
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files != null && files.Length > 0)
                {
                    string droppedPath = files[0];
                    if (File.Exists(droppedPath) || Directory.Exists(droppedPath))
                    {
                        targetBox.Text = droppedPath;
                    }
                }
            }
        }

        /// <summary>
        /// OpenFileDialogでURLを指定した際にWindowsがINetCacheにダウンロードしたファイルを
        /// 処理完了後にセキュア削除する。機密データがブラウザキャッシュに残留するのを防止する。
        /// </summary>
        private void CleanupINetCacheFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return;
            if (filePath.IndexOf("INetCache", StringComparison.OrdinalIgnoreCase) >= 0 ||
                filePath.IndexOf("Temporary Internet Files", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                try
                {
                    GpgService.SecureDeleteFile(filePath);
                }
                catch { /* キャッシュ削除失敗は無視 */ }
            }
        }
    }
}
