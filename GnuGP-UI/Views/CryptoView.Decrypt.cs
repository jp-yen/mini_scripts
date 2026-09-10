using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GpgUi.Views
{
    public partial class CryptoView : UserControl
    {
        private UIElement CreateDecryptPanel()
        {
            var grid = new Grid { Margin = new Thickness(12) };
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            _txtDecryptInput = ThemeHelper.CreateStyledTextBox();
            _txtDecryptInput.MinHeight = 120;
            _txtDecryptInput.AcceptsReturn = true;
            _txtDecryptInput.TextWrapping = TextWrapping.Wrap;
            _txtDecryptInput.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            _txtDecryptInput.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            _txtDecryptInput.FontFamily = new FontFamily("Consolas");
            _txtDecryptInput.VerticalContentAlignment = VerticalAlignment.Top;
            _txtDecryptInput.Padding = new Thickness(6, 6, 6, 6);
            _txtDecryptInput.AllowDrop = true;
            _txtDecryptInput.PreviewDragOver += (s, e) => { e.Effects = DragDropEffects.Copy; e.Handled = true; };
            _txtDecryptInput.Drop += async (s, e) => { e.Handled = true; await HandleDecryptDropAsync(e); };

            var inHeaderStack = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            inHeaderStack.Children.Add(new TextBlock { Text = "復号化する暗号化メッセージまたはファイル (ファイルドロップ可):", Foreground = ThemeHelper.BrushTextMuted, FontSize = 13, VerticalAlignment = VerticalAlignment.Center });
            var btnBrowseDecrypt = ThemeHelper.CreateSecondaryButton("ファイル選択", async (s, e) => await OnDecryptFileClickAsync());
            btnBrowseDecrypt.Height = 28;
            btnBrowseDecrypt.Margin = new Thickness(12, 0, 0, 0);
            inHeaderStack.Children.Add(btnBrowseDecrypt);

            var inPnl = new StackPanel();
            inPnl.Children.Add(inHeaderStack);
            inPnl.Children.Add(_txtDecryptInput);

            var inputCard = ThemeHelper.CreateCardPanel(inPnl, 12);
            Grid.SetRow(inputCard, 0);
            grid.Children.Add(inputCard);

            var passPnl = new StackPanel { Margin = new Thickness(0, 0, 0, 4) };
            passPnl.Children.Add(new TextBlock { Text = "パスフレーズ (秘密鍵または共通鍵用):", Foreground = ThemeHelper.BrushTextMuted, FontSize = 12, Margin = new Thickness(0, 0, 0, 4) });
            _txtDecryptPassphrase = ThemeHelper.CreateStyledPasswordBox();
            passPnl.Children.Add(_txtDecryptPassphrase);

            var passCard = ThemeHelper.CreateCardPanel(passPnl, 10);
            Grid.SetRow(passCard, 1);
            grid.Children.Add(passCard);

            var actBar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 6, 6, 6) };
            actBar.Children.Add(ThemeHelper.CreatePrimaryButton("🔓 復号化を実行", async (s, e) => await OnDecryptClickAsync()));
            Grid.SetRow(actBar, 2);
            grid.Children.Add(actBar);

            _lblSignerInfo = new TextBlock { FontSize = 13, Foreground = ThemeHelper.BrushSuccessGreen, TextWrapping = TextWrapping.Wrap };
            _signerInfoCard = ThemeHelper.CreateCardPanel(_lblSignerInfo, 10);
            _signerInfoCard.Visibility = Visibility.Collapsed;
            _signerInfoCard.Margin = new Thickness(0, 0, 0, 10);
            Grid.SetRow(_signerInfoCard, 3);
            grid.Children.Add(_signerInfoCard);

            _txtDecryptOutput = ThemeHelper.CreateStyledTextBox("", true);
            _txtDecryptOutput.MinHeight = 120;
            _txtDecryptOutput.AcceptsReturn = true;
            _txtDecryptOutput.TextWrapping = TextWrapping.Wrap;
            _txtDecryptOutput.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            _txtDecryptOutput.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            _txtDecryptOutput.FontFamily = new FontFamily("Consolas");
            _txtDecryptOutput.VerticalContentAlignment = VerticalAlignment.Top;
            _txtDecryptOutput.Padding = new Thickness(6, 6, 6, 6);

            var outBtnStack = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            outBtnStack.Children.Add(ThemeHelper.CreateSecondaryButton("復号文をコピー", (s, e) => CopyText(_txtDecryptOutput.Text)));
            outBtnStack.Children.Add(ThemeHelper.CreateSecondaryButton("テキストに保存...", (s, e) => SaveTextToFile(_txtDecryptOutput.Text, "decrypted.txt")));
            _btnSaveToSameFolder = ThemeHelper.CreatePrimaryButton("元フォルダに保存...", async (s, e) => await SaveToSameFolderAsync());
            outBtnStack.Children.Add(_btnSaveToSameFolder);

            var outPnl = new StackPanel();
            outPnl.Children.Add(new TextBlock { Text = "復号化出力結果:", Foreground = ThemeHelper.BrushTextMuted, FontSize = 13, Margin = new Thickness(0, 0, 0, 6) });
            outPnl.Children.Add(_txtDecryptOutput);
            outPnl.Children.Add(outBtnStack);

            var outputCard = ThemeHelper.CreateCardPanel(outPnl, 12);
            Grid.SetRow(outputCard, 4);
            grid.Children.Add(outputCard);

            return new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = grid
            };
        }

        private async Task OnDecryptClickAsync()
        {
            string inputStr = _txtDecryptInput.Text.Trim();
            if (string.IsNullOrEmpty(inputStr))
            {
                MessageBox.Show("暗号化された ASCII Armor メッセージまたはファイルパスを入力してください。", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string pass = _txtDecryptPassphrase.Password;

            string targetFile = null;
            if (File.Exists(inputStr))
            {
                targetFile = inputStr;
            }
            else if (!string.IsNullOrEmpty(_lastSourceFilePath) && File.Exists(_lastSourceFilePath))
            {
                targetFile = _lastSourceFilePath;
            }

            if (targetFile != null)
            {
                await DecryptAndSaveFileAsync(targetFile, pass);
            }
            else
            {
                var res = await _gpgService.DecryptTextAsync(inputStr, pass);
                if (res.Success)
                {
                    _txtDecryptOutput.Text = res.OutputText;

                    if (!string.IsNullOrEmpty(res.SignerInfo))
                    {
                        _signerInfoCard.Visibility = Visibility.Visible;
                        _lblSignerInfo.Text = "ステータス: " + res.SignerInfo;
                    }
                    else
                    {
                        _signerInfoCard.Visibility = Visibility.Collapsed;
                    }
                }
                else
                {
                    MessageBox.Show("復号化に失敗しました:\n" + res.ErrorMessage, "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async Task OnDecryptFileClickAsync()
        {
            var ofd = new Microsoft.Win32.OpenFileDialog { Title = "復号化する暗号化ファイル (.asc, .gpg) を選択" };
            if (ofd.ShowDialog() != true) return;

            string pass = _txtDecryptPassphrase.Password;
            await DecryptAndSaveFileAsync(ofd.FileName, pass);
        }

        private async Task DecryptAndSaveFileAsync(string inputFilePath, string passphrase)
        {
            if (string.IsNullOrEmpty(inputFilePath) || !File.Exists(inputFilePath)) return;
            _lastSourceFilePath = inputFilePath;

            string dir = Path.GetDirectoryName(inputFilePath);
            string fileName = Path.GetFileName(inputFilePath);

            if (fileName.EndsWith(".zip.asc", StringComparison.OrdinalIgnoreCase))
                fileName = fileName.Substring(0, fileName.Length - 8) + ".zip";
            else if (fileName.EndsWith(".zip.gpg", StringComparison.OrdinalIgnoreCase))
                fileName = fileName.Substring(0, fileName.Length - 8) + ".zip";
            else if (fileName.EndsWith(".asc", StringComparison.OrdinalIgnoreCase))
                fileName = fileName.Substring(0, fileName.Length - 4);
            else if (fileName.EndsWith(".gpg", StringComparison.OrdinalIgnoreCase))
                fileName = fileName.Substring(0, fileName.Length - 4);
            else if (fileName.EndsWith(".pgp", StringComparison.OrdinalIgnoreCase))
                fileName = fileName.Substring(0, fileName.Length - 4);
            else
                fileName += ".decrypted";

            var sfd = new Microsoft.Win32.SaveFileDialog
            {
                Title = "復号化ファイルの保存先を選択",
                InitialDirectory = dir,
                FileName = fileName,
                Filter = "すべてのファイル (*.*)|*.*"
            };

            if (sfd.ShowDialog() != true)
            {
                return;
            }

            string targetPath = sfd.FileName;

            var res = await _gpgService.DecryptFileToPathAsync(inputFilePath, targetPath, passphrase);

            if (res.Success)
            {
                _signerInfoCard.Visibility = Visibility.Visible;
                string statusMsg = string.Format("📁 復号化されたファイルを次の場所に保存しました:\n{0}", targetPath);
                if (!string.IsNullOrEmpty(res.SignerInfo))
                {
                    statusMsg = "ステータス: " + res.SignerInfo + "\n\n" + statusMsg;
                }
                _lblSignerInfo.Text = statusMsg;

                try
                {
                    string text = File.ReadAllText(targetPath, Encoding.UTF8);
                    if (!text.Contains("\0"))
                    {
                        _txtDecryptOutput.Text = text;
                    }
                    else
                    {
                        _txtDecryptOutput.Text = string.Format("[バイナリファイルの復号化完了]\n保存先: {0}", targetPath);
                    }
                }
                catch
                {
                    _txtDecryptOutput.Text = string.Format("[バイナリファイルの復号化完了]\n保存先: {0}", targetPath);
                }

                MessageBox.Show(string.Format("ファイルを正常に復号化し、次の場所に保存しました:\n{0}", targetPath), "復号化成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show("ファイルの復号化に失敗しました:\n" + res.ErrorMessage, "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task SaveToSameFolderAsync()
        {
            if (string.IsNullOrEmpty(_txtDecryptOutput.Text) && (string.IsNullOrEmpty(_lastSourceFilePath) || !File.Exists(_lastSourceFilePath)))
            {
                MessageBox.Show("復号化結果が空です。", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!string.IsNullOrEmpty(_lastSourceFilePath) && File.Exists(_lastSourceFilePath))
            {
                await DecryptAndSaveFileAsync(_lastSourceFilePath, _txtDecryptPassphrase.Password);
                return;
            }

            var sfd = new Microsoft.Win32.SaveFileDialog
            {
                Title = "復号化テキストの保存先を選択",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                FileName = "decrypted.txt",
                Filter = "テキストファイル (*.txt)|*.txt|すべてのファイル (*.*)|*.*"
            };

            if (sfd.ShowDialog() == true)
            {
                try
                {
                    File.WriteAllText(sfd.FileName, _txtDecryptOutput.Text, Encoding.UTF8);
                    MessageBox.Show("復号化ファイルを保存しました:\n" + sfd.FileName, "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("ファイルの保存に失敗しました: " + ex.Message, "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }
}
