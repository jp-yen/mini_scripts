using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;

namespace GpgUi.Views
{
    public partial class CryptoView : UserControl
    {
        private UIElement CreateEncryptFilePanel()
        {
            var grid = new Grid { Margin = new Thickness(12) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var leftStack = new Grid { Margin = new Thickness(0, 0, 16, 0) };
            leftStack.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            leftStack.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            leftStack.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var filePanel = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };

            _lblEncryptSelectedPath = new TextBlock
            {
                Text = "選択中の対象: (未選択)",
                Foreground = ThemeHelper.BrushPrimaryCyan,
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 8),
                TextWrapping = TextWrapping.Wrap
            };
            filePanel.Children.Add(_lblEncryptSelectedPath);

            var dropButtonsGrid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 4, 0, 0) };
            var btnFile = ThemeHelper.CreateSecondaryButton("📄 ファイル選択", async (s, e) => await OnEncryptFileClickAsync());
            btnFile.Margin = new Thickness(0, 0, 4, 0);
            var btnFolder = ThemeHelper.CreateSecondaryButton("📁 フォルダー選択", async (s, e) => await OnEncryptFolderClickAsync());
            btnFolder.Margin = new Thickness(4, 0, 0, 0);
            dropButtonsGrid.Children.Add(btnFile);
            dropButtonsGrid.Children.Add(btnFolder);

            var dropContentStack = new StackPanel();
            dropContentStack.Children.Add(new TextBlock
            {
                Text = "📁 ここにドラッグ＆ドロップ",
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = ThemeHelper.BrushTextMain,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 10)
            });
            dropContentStack.Children.Add(dropButtonsGrid);

            var fileDropCard = ThemeHelper.CreateCardPanel(dropContentStack, 14);
            fileDropCard.AllowDrop = true;
            fileDropCard.PreviewDragOver += (s, e) => { e.Effects = DragDropEffects.Copy; e.Handled = true; };
            fileDropCard.Drop += async (s, e) => { e.Handled = true; await HandleEncryptDropAsync(e); };
            filePanel.Children.Add(fileDropCard);

            // 出力形式選択
            var formatPanel = new Grid { Margin = new Thickness(0, 10, 0, 0) };
            formatPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            formatPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var formatLabel = new TextBlock
            {
                Text = "出力形式:",
                Foreground = ThemeHelper.BrushTextMuted,
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            Grid.SetColumn(formatLabel, 0);
            formatPanel.Children.Add(formatLabel);
            _cboOutputFormatFile = new ComboBox
            {
                Height = 36,
                Background = ThemeHelper.BrushBgDark,
                Foreground = ThemeHelper.BrushTextMain,
                BorderBrush = ThemeHelper.BrushBorder,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            _cboOutputFormatFile.Items.Add("ASCII Armor (.asc) - テキスト形式");
            _cboOutputFormatFile.Items.Add("バイナリ (.gpg) - コンパクト形式");
            _cboOutputFormatFile.SelectedIndex = 0;
            Grid.SetColumn(_cboOutputFormatFile, 1);
            formatPanel.Children.Add(_cboOutputFormatFile);
            filePanel.Children.Add(formatPanel);

            var btnExecuteEncryptFile = ThemeHelper.CreatePrimaryButton("🔒 暗号化を実行", async (s, e) => await EncryptFilesOrDirectoryAsync(_lastEncryptSourcePaths));
            btnExecuteEncryptFile.Margin = new Thickness(0, 10, 0, 0);
            filePanel.Children.Add(btnExecuteEncryptFile);

            Grid.SetRow(filePanel, 0);
            leftStack.Children.Add(filePanel);

            // 送信者の鍵 (署名) パネル
            var signPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            signPanel.Children.Add(new TextBlock { Text = "送信者の鍵 (署名用)", FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = ThemeHelper.BrushTextMain, Margin = new Thickness(6, 0, 0, 6) });

            _cboSignKeyFile = new ComboBox
            {
                Height = 36,
                Background = ThemeHelper.BrushBgDark,
                Foreground = ThemeHelper.BrushTextMain,
                BorderBrush = ThemeHelper.BrushBorder,
                VerticalContentAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 8)
            };
            signPanel.Children.Add(_cboSignKeyFile);

            signPanel.Children.Add(new TextBlock { Text = "署名用パスフレーズ:", FontSize = 12, Foreground = ThemeHelper.BrushTextMuted, Margin = new Thickness(6, 0, 0, 4) });
            _txtSignPassphraseFile = ThemeHelper.CreateStyledPasswordBox();
            signPanel.Children.Add(_txtSignPassphraseFile);

            var signCard = ThemeHelper.CreateCardPanel(signPanel, 12);
            Grid.SetRow(signCard, 1);
            leftStack.Children.Add(signCard);

            Grid.SetColumn(leftStack, 0);
            grid.Children.Add(leftStack);

            // 右側: 鍵選択
            var rightCard = CreateRightKeyPanel(out _lstRecipientsFile, out _txtRecipientSearchFile, out _lblRecipientCountFile, out _txtSymmetricPassphraseFile);
            Grid.SetColumn(rightCard, 1);
            grid.Children.Add(rightCard);

            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = grid
            };
            return scroll;
        }

        private UIElement CreateEncryptTextPanel()
        {
            var grid = new Grid { Margin = new Thickness(12) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var leftStack = new Grid { Margin = new Thickness(0, 0, 16, 0) };
            leftStack.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            leftStack.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            leftStack.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            leftStack.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // 1. テキスト入力
            var inPnl = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            inPnl.Children.Add(new TextBlock { Text = "暗号化する平文テキスト:", Foreground = ThemeHelper.BrushTextMuted, FontSize = 13, Margin = new Thickness(0, 0, 0, 6) });
            _txtPlaintextInput = ThemeHelper.CreateStyledTextBox();
            _txtPlaintextInput.MinHeight = 110;
            _txtPlaintextInput.AcceptsReturn = true;
            _txtPlaintextInput.TextWrapping = TextWrapping.Wrap;
            _txtPlaintextInput.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            _txtPlaintextInput.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            _txtPlaintextInput.FontFamily = new FontFamily("Consolas");
            _txtPlaintextInput.VerticalContentAlignment = VerticalAlignment.Top;
            _txtPlaintextInput.Padding = new Thickness(6, 6, 6, 6);
            inPnl.Children.Add(_txtPlaintextInput);

            Grid.SetRow(inPnl, 0);
            leftStack.Children.Add(inPnl);

            // 2. 送信者の鍵
            var signPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            signPanel.Children.Add(new TextBlock { Text = "送信者の鍵 (署名用)", FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = ThemeHelper.BrushTextMain, Margin = new Thickness(6, 0, 0, 6) });

            _cboSignKeyText = new ComboBox
            {
                Height = 36,
                Background = ThemeHelper.BrushBgDark,
                Foreground = ThemeHelper.BrushTextMain,
                BorderBrush = ThemeHelper.BrushBorder,
                VerticalContentAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 8)
            };
            signPanel.Children.Add(_cboSignKeyText);

            signPanel.Children.Add(new TextBlock { Text = "署名用パスフレーズ:", FontSize = 12, Foreground = ThemeHelper.BrushTextMuted, Margin = new Thickness(6, 0, 0, 4) });
            _txtSignPassphraseText = ThemeHelper.CreateStyledPasswordBox();
            signPanel.Children.Add(_txtSignPassphraseText);

            var signCard = ThemeHelper.CreateCardPanel(signPanel, 12);
            Grid.SetRow(signCard, 1);
            leftStack.Children.Add(signCard);

            // 3. 暗号化ボタン
            var btnEncryptText = ThemeHelper.CreatePrimaryButton("🔒 テキストを暗号化", async (s, e) => await OnEncryptTextClickAsync());
            btnEncryptText.Margin = new Thickness(0, 0, 0, 12);
            Grid.SetRow(btnEncryptText, 2);
            leftStack.Children.Add(btnEncryptText);

            // 4. 暗号化出力結果
            _txtEncryptOutput = ThemeHelper.CreateStyledTextBox("", true);
            _txtEncryptOutput.MinHeight = 130;
            _txtEncryptOutput.AcceptsReturn = true;
            _txtEncryptOutput.TextWrapping = TextWrapping.Wrap;
            _txtEncryptOutput.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            _txtEncryptOutput.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            _txtEncryptOutput.FontFamily = new FontFamily("Consolas");
            _txtEncryptOutput.VerticalContentAlignment = VerticalAlignment.Top;
            _txtEncryptOutput.Padding = new Thickness(6, 6, 6, 6);

            var outBtnStack = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            outBtnStack.Children.Add(ThemeHelper.CreateSecondaryButton("暗号文をコピー", (s, e) => CopyText(_txtEncryptOutput.Text)));
            outBtnStack.Children.Add(ThemeHelper.CreateSecondaryButton(".asc ファイルに保存", (s, e) => SaveTextToFile(_txtEncryptOutput.Text, "encrypted.asc")));

            var outPnl = new StackPanel();
            outPnl.Children.Add(new TextBlock { Text = "暗号化出力結果 (ASCII Armor):", Foreground = ThemeHelper.BrushTextMuted, FontSize = 13, Margin = new Thickness(0, 0, 0, 6) });
            outPnl.Children.Add(_txtEncryptOutput);
            outPnl.Children.Add(outBtnStack);

            var outputCard = ThemeHelper.CreateCardPanel(outPnl, 12);
            Grid.SetRow(outputCard, 3);
            leftStack.Children.Add(outputCard);

            Grid.SetColumn(leftStack, 0);
            grid.Children.Add(leftStack);

            // 右側: 鍵選択
            var rightCard = CreateRightKeyPanel(out _lstRecipientsText, out _txtRecipientSearchText, out _lblRecipientCountText, out _txtSymmetricPassphraseText);
            Grid.SetColumn(rightCard, 1);
            grid.Children.Add(rightCard);

            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = grid
            };
            return scroll;
        }

        private Border CreateRightKeyPanel(out ListView lstRecipients, out TextBox txtRecipientSearch, out TextBlock lblRecipientCount, out PasswordBox txtSymmetricPassphrase)
        {
            var rightStack = new Grid();
            rightStack.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            rightStack.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var encryptModeTabs = new TabControl
            {
                Background = ThemeHelper.BrushBgDark,
                BorderBrush = ThemeHelper.BrushBorder,
                Foreground = ThemeHelper.BrushTextMain
            };

            var pnlPubKey = new StackPanel();

            var searchHeaderGrid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            searchHeaderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            searchHeaderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var searchBox = ThemeHelper.CreateStyledTextBox();
            searchBox.Height = 32;
            searchBox.Padding = new Thickness(6, 2, 6, 2);
            Grid.SetColumn(searchBox, 0);

            var lblCount = new TextBlock
            {
                Text = "(選択: 0 件)",
                Foreground = ThemeHelper.BrushPrimaryCyan,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            };
            Grid.SetColumn(lblCount, 1);

            searchHeaderGrid.Children.Add(searchBox);
            searchHeaderGrid.Children.Add(lblCount);
            pnlPubKey.Children.Add(searchHeaderGrid);

            var listView = new ListView
            {
                Background = ThemeHelper.BrushSurfaceDark,
                Foreground = ThemeHelper.BrushTextMain,
                BorderBrush = ThemeHelper.BrushBorder,
                BorderThickness = new Thickness(1),
                Height = 300,
                SelectionMode = SelectionMode.Multiple
            };

            var gridView = new GridView();
            var colTitle = new GridViewColumn { Header = "受信者 (名前 / メール)", Width = 320, DisplayMemberBinding = new Binding("DisplayTitle") };
            var colTrust = new GridViewColumn { Header = "状態", Width = 90, DisplayMemberBinding = new Binding("StatusDisplayText") };

            gridView.Columns.Add(colTitle);
            gridView.Columns.Add(colTrust);

            var itemStyle = new Style(typeof(ListViewItem));
            itemStyle.Setters.Add(new Setter(Control.ForegroundProperty, ThemeHelper.BrushTextMain));
            itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6, 6, 6, 6)));
            itemStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Left));
            itemStyle.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));

            var template = new ControlTemplate(typeof(ListViewItem));
            var border = new FrameworkElementFactory(typeof(Border));
            border.Name = "bd";
            border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            border.SetBinding(Border.PaddingProperty, new Binding("Padding") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
            border.SetValue(Border.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(51, 65, 85)));
            border.SetValue(Border.BorderThicknessProperty, new Thickness(0, 0, 0, 1));
            border.SetBinding(Control.ForegroundProperty, new Binding("Foreground") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
            border.AppendChild(new FrameworkElementFactory(typeof(GridViewRowPresenter)));
            template.VisualTree = border;

            // 選択時: 鮮やかなシアンの左ボーダーと視認性の高いダークブルー背景
            var selTrigger = new Trigger { Property = ListViewItem.IsSelectedProperty, Value = true };
            selTrigger.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(12, 74, 110))) { TargetName = "bd" });
            selTrigger.Setters.Add(new Setter(Border.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(6, 182, 212))) { TargetName = "bd" });
            selTrigger.Setters.Add(new Setter(Border.BorderThicknessProperty, new Thickness(3, 0, 0, 1)) { TargetName = "bd" });
            selTrigger.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
            template.Triggers.Add(selTrigger);

            // ホバー時
            var hoverTrigger = new Trigger { Property = ListViewItem.IsMouseOverProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(30, 58, 79))) { TargetName = "bd" });
            hoverTrigger.Setters.Add(new Setter(Control.ForegroundProperty, new SolidColorBrush(Color.FromRgb(56, 189, 248))));
            template.Triggers.Add(hoverTrigger);

            // 選択中 + ホバー
            var selHover = new MultiTrigger();
            selHover.Conditions.Add(new Condition { Property = ListViewItem.IsSelectedProperty, Value = true });
            selHover.Conditions.Add(new Condition { Property = ListViewItem.IsMouseOverProperty, Value = true });
            selHover.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(3, 105, 161))) { TargetName = "bd" });
            selHover.Setters.Add(new Setter(Border.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(6, 182, 212))) { TargetName = "bd" });
            selHover.Setters.Add(new Setter(Border.BorderThicknessProperty, new Thickness(3, 0, 0, 1)) { TargetName = "bd" });
            selHover.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
            template.Triggers.Add(selHover);

            // 暗号化不可（期限切れ等）の鍵は薄く表示
            var isUsableTrigger = new DataTrigger
            {
                Binding = new Binding("IsUsableForEncryption"),
                Value = false
            };
            isUsableTrigger.Setters.Add(new Setter(Control.ForegroundProperty, ThemeHelper.BrushTextMuted));
            isUsableTrigger.Setters.Add(new Setter(UIElement.OpacityProperty, 0.5));
            itemStyle.Triggers.Add(isUsableTrigger);

            itemStyle.Setters.Add(new Setter(Control.TemplateProperty, template));
            listView.ItemContainerStyle = itemStyle;
            listView.View = gridView;

            searchBox.TextChanged += (s, e) => UpdateRecipientList(listView, searchBox.Text);

            pnlPubKey.Children.Add(listView);

            var tabPubKey = new TabItem
            {
                Header = "公開鍵",
                Padding = new Thickness(14, 6, 14, 6),
                FontSize = 12
            };
            tabPubKey.Content = pnlPubKey;

            var pnlSym = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
            pnlSym.Children.Add(new TextBlock { Text = "共通鍵 (パスフレーズ) を入力:", Foreground = ThemeHelper.BrushTextMuted, FontSize = 12, Margin = new Thickness(0, 0, 0, 6) });
            var symPassBox = ThemeHelper.CreateStyledPasswordBox();
            pnlSym.Children.Add(symPassBox);
            pnlSym.Children.Add(new TextBlock { Text = "※公開鍵未選択の場合、このパスフレーズで共通鍵暗号化されます。", Foreground = ThemeHelper.BrushTextMuted, FontSize = 11, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap });

            var tabSym = new TabItem
            {
                Header = "共通鍵",
                Padding = new Thickness(14, 6, 14, 6),
                FontSize = 12
            };
            tabSym.Content = pnlSym;

            encryptModeTabs.Items.Add(tabPubKey);
            encryptModeTabs.Items.Add(tabSym);

            Grid.SetRow(encryptModeTabs, 0);
            rightStack.Children.Add(encryptModeTabs);

            listView.SelectionChanged += (s, e) =>
            {
                if (_isUpdatingSelection) return;

                foreach (GpgKey added in e.AddedItems)
                {
                    if (added != null && added.IsUsableForEncryption && !string.IsNullOrEmpty(added.Fingerprint))
                    {
                        _selectedRecipientFingerprints.Add(added.Fingerprint);
                    }
                    else if (added != null && !added.IsUsableForEncryption)
                    {
                        MessageBox.Show(
                            string.Format("選択された鍵 '{0}' は暗号化に使用できません。\n理由: {1}\n\n信頼度が不足している場合は、鍵マネージャーから署名を行うか信頼設定を変更してください。", added.DisplayTitle, added.UnusableReason),
                            "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                        listView.SelectedItems.Remove(added);
                    }
                }

                foreach (GpgKey removed in e.RemovedItems)
                {
                    if (removed != null && !string.IsNullOrEmpty(removed.Fingerprint))
                    {
                        _selectedRecipientFingerprints.Remove(removed.Fingerprint);
                    }
                }

                UpdateRecipientCountDisplay();
            };

            lstRecipients = listView;
            txtRecipientSearch = searchBox;
            lblRecipientCount = lblCount;
            txtSymmetricPassphrase = symPassBox;

            return ThemeHelper.CreateCardPanel(rightStack, 12);
        }

        private async Task OnEncryptFileClickAsync()
        {
            var ofd = new Microsoft.Win32.OpenFileDialog
            {
                Title = "暗号化するファイルを選択 (複数選択可)",
                Multiselect = true
            };
            if (ofd.ShowDialog() == true)
            {
                await OpenFilesSmartAsync(ofd.FileNames);
            }
        }

        private async Task OnEncryptFolderClickAsync()
        {
            var fbd = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "暗号化するフォルダーを選択してください（自動的にZIP圧縮してから暗号化します）",
                ShowNewFolderButton = false
            };

            if (fbd.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                await OpenFilesSmartAsync(new[] { fbd.SelectedPath });
            }
        }

        private async Task OnEncryptTextClickAsync()
        {
            string plainText = _txtPlaintextInput.Text;
            if (string.IsNullOrEmpty(plainText))
            {
                MessageBox.Show("暗号化するテキストを入力してください。", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var recipients = GetSelectedRecipientFingerprints();
            string symPass = _txtSymmetricPassphraseText.Password;

            if (recipients.Count == 0 && string.IsNullOrEmpty(symPass))
            {
                MessageBox.Show("受信者の公開鍵を1つ以上選択するか、共通鍵暗号化のパスフレーズを入力してください。", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string signKeyFpr = GetSelectedSignKeyFingerprint(_cboSignKeyText);
            string signPass = _txtSignPassphraseText != null ? _txtSignPassphraseText.Password : "";

            var res = await _gpgService.EncryptTextAsync(plainText, recipients, symPass, signKeyFpr, signPass);
            if (res.Success)
            {
                _txtEncryptOutput.Text = res.OutputText;
            }
            else
            {
                MessageBox.Show("暗号化に失敗しました:\n" + res.ErrorMessage, "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task EncryptFilesOrDirectoryAsync(List<string> sourcePaths)
        {
            if (sourcePaths == null || sourcePaths.Count == 0)
            {
                MessageBox.Show("暗号化するファイルまたはフォルダーを選択してください。", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var validPaths = sourcePaths.Where(p => !string.IsNullOrEmpty(p) && (File.Exists(p) || Directory.Exists(p))).ToList();
            if (validPaths.Count == 0)
            {
                MessageBox.Show("指定されたファイルまたはフォルダーが存在しません。", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var recipients = GetSelectedRecipientFingerprints();
            string symPass = _txtSymmetricPassphraseFile.Password;

            if (recipients.Count == 0 && string.IsNullOrEmpty(symPass))
            {
                MessageBox.Show("受信者の公開鍵を1つ以上選択するか、共通鍵暗号化のパスフレーズを入力してください。", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            bool useBinary = _cboOutputFormatFile != null && _cboOutputFormatFile.SelectedIndex == 1;
            string ext = useBinary ? ".gpg" : ".asc";

            bool isSingleItem = validPaths.Count == 1;
            bool isSingleDir = isSingleItem && Directory.Exists(validPaths[0]);
            bool isSingleFile = isSingleItem && File.Exists(validPaths[0]);
            bool isMultiple = validPaths.Count > 1;

            string defaultName;
            string initDir;
            string dialogTitle;

            if (isSingleFile)
            {
                string single = validPaths[0];
                defaultName = Path.GetFileName(single) + ext;
                initDir = Path.GetDirectoryName(single);
                dialogTitle = "ファイルの暗号化ファイルの保存先を選択";
            }
            else if (isSingleDir)
            {
                string single = validPaths[0];
                string dirName = Path.GetFileName(single.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (string.IsNullOrEmpty(dirName)) dirName = "folder";
                defaultName = dirName + ".zip" + ext;
                initDir = Path.GetDirectoryName(single.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                dialogTitle = "フォルダーの暗号化ファイルの保存先を選択";
            }
            else
            {
                // 複数ファイル・フォルダー
                string first = validPaths[0];
                string baseName = File.Exists(first)
                    ? Path.GetFileNameWithoutExtension(first) + "_archive"
                    : Path.GetFileName(first.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) + "_archive";
                defaultName = baseName + ".zip" + ext;
                initDir = Path.GetDirectoryName(first.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                dialogTitle = string.Format("複数アイテム ({0}件) の暗号化ファイルの保存先を選択", validPaths.Count);
            }

            string filterAsc = "ASCII Armor 暗号化ファイル (*.asc)|*.asc";
            string filterGpg = "GPG バイナリ暗号化ファイル (*.gpg)|*.gpg";
            string filterAll = "すべてのファイル (*.*)|*.*";
            string filter = useBinary
                ? string.Format("{0}|{1}|{2}", filterGpg, filterAsc, filterAll)
                : string.Format("{0}|{1}|{2}", filterAsc, filterGpg, filterAll);

            var sfd = new Microsoft.Win32.SaveFileDialog
            {
                Title = dialogTitle,
                InitialDirectory = initDir,
                FileName = defaultName,
                Filter = filter
            };

            if (sfd.ShowDialog() != true) return;

            string targetGpgPath = sfd.FileName;

            string signKeyFpr = GetSelectedSignKeyFingerprint(_cboSignKeyFile);
            string signPass = _txtSignPassphraseFile != null ? _txtSignPassphraseFile.Password : "";

            string tempZipPath = null;
            string fileToEncrypt;

            try
            {
                if (isSingleFile)
                {
                    fileToEncrypt = validPaths[0];
                }
                else if (isSingleDir)
                {
                    tempZipPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".zip");
                    System.IO.Compression.ZipFile.CreateFromDirectory(validPaths[0], tempZipPath, System.IO.Compression.CompressionLevel.Optimal, false);
                    fileToEncrypt = tempZipPath;
                }
                else
                {
                    // 複数ファイル/フォルダーを 1 つの ZIP にまとめる
                    tempZipPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".zip");
                    CreateZipFromMultipleSources(validPaths, tempZipPath);
                    fileToEncrypt = tempZipPath;
                }

                var res = await _gpgService.EncryptFileAsync(fileToEncrypt, targetGpgPath, recipients, symPass, signKeyFpr, signPass);
                if (res.Success)
                {
                    string successMsg = isMultiple
                        ? string.Format("{0} 件のアイテムを 1 つの ZIP アーカイブにまとめて正常に暗号化しました:\n{1}", validPaths.Count, targetGpgPath)
                        : string.Format("正常に暗号化して保存しました:\n{0}", targetGpgPath);
                    MessageBox.Show(successMsg, "暗号化成功", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("暗号化に失敗しました:\n" + res.ErrorMessage, "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("暗号化処理中にエラーが発生しました: " + ex.Message, "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                if (!string.IsNullOrEmpty(tempZipPath) && File.Exists(tempZipPath))
                {
                    GpgService.SecureDeleteFile(tempZipPath);
                }
            }
        }

        private static void CreateZipFromMultipleSources(List<string> sourcePaths, string destinationZipPath)
        {
            using (var zipStream = new FileStream(destinationZipPath, FileMode.Create))
            using (var archive = new System.IO.Compression.ZipArchive(zipStream, System.IO.Compression.ZipArchiveMode.Create))
            {
                var usedEntryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var path in sourcePaths)
                {
                    if (File.Exists(path))
                    {
                        string baseName = Path.GetFileName(path);
                        string entryName = GetUniqueEntryName(usedEntryNames, baseName);
                        AddFileToZipArchive(archive, path, entryName);
                    }
                    else if (Directory.Exists(path))
                    {
                        string dirName = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                        if (string.IsNullOrEmpty(dirName)) dirName = "folder";
                        dirName = GetUniqueEntryName(usedEntryNames, dirName);

                        var files = Directory.GetFiles(path, "*", SearchOption.AllDirectories);
                        foreach (var file in files)
                        {
                            string relPath = file.Substring(path.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                            string entryName = Path.Combine(dirName, relPath).Replace('\\', '/');
                            AddFileToZipArchive(archive, file, entryName);
                        }
                    }
                }
            }
        }

        private static void AddFileToZipArchive(System.IO.Compression.ZipArchive archive, string sourceFilePath, string entryName)
        {
            var entry = archive.CreateEntry(entryName, System.IO.Compression.CompressionLevel.Optimal);
            using (var fs = new FileStream(sourceFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var es = entry.Open())
            {
                fs.CopyTo(es);
            }
        }

        private static string GetUniqueEntryName(HashSet<string> used, string fileName)
        {
            if (!used.Contains(fileName))
            {
                used.Add(fileName);
                return fileName;
            }

            string nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
            string ext = Path.GetExtension(fileName);
            int count = 1;
            while (true)
            {
                string candidate = string.Format("{0} ({1}){2}", nameWithoutExt, count, ext);
                if (!used.Contains(candidate))
                {
                    used.Add(candidate);
                    return candidate;
                }
                count++;
            }
        }
    }
}
