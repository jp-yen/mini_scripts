using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GpgUi.Views
{
    public class KeyImportWindow : Window
    {
        private readonly GpgService _gpgService;
        private readonly ObservableCollection<KeyFileItem> _selectedFiles = new ObservableCollection<KeyFileItem>();

        private TabControl _modeTabControl;
        private ListView _lstFileItems;
        private Border _cardFileStatus;
        private TextBlock _lblFileStatus;
        private TextBlock _txtFileResultDetail;

        private TextBox _txtArmorInput;
        private Border _cardArmorStatus;
        private TextBlock _lblArmorStatus;
        private TextBlock _txtArmorResultDetail;

        private TextBox _txtKeyIdsInput;
        private ComboBox _cboKeyserver;
        private Border _cardKeyserverStatus;
        private TextBlock _lblKeyIdsStatus;
        private TextBlock _txtKeyserverResultDetail;

        private TextBlock _lblStatusSummary;
        private Button _btnImport;

        public bool IsKeyImported { get; private set; }

        public KeyImportWindow(GpgService gpgService) : this(gpgService, null)
        {
        }

        public KeyImportWindow(GpgService gpgService, IEnumerable<string> initialPaths)
        {
            _gpgService = gpgService;
            IsKeyImported = false;

            Title = "GnuPG 公開鍵・秘密鍵の複数同時インポート";
            Width = 920;
            Height = 720;
            MinHeight = 540;
            MinWidth = 720;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = ThemeHelper.BrushBgDark;
            AllowDrop = true;

            Drop += Window_Drop;

            InitializeUI();
            ThemeHelper.ApplyDarkTitleBar(this);

            if (initialPaths != null && initialPaths.Any())
            {
                AddSelectedFiles(initialPaths);
            }
        }

        private void InitializeUI()
        {
            var mainGrid = new Grid { Margin = new Thickness(16) };
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // ヘッダー
            var headerStack = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            headerStack.Children.Add(new TextBlock
            {
                Text = "🔑 公開鍵・秘密鍵の複数一括追加",
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                Foreground = ThemeHelper.BrushPrimaryCyan
            });
            headerStack.Children.Add(new TextBlock
            {
                Text = "複数の鍵ファイル、複数の ASCII Armor ブロック、または複数の Key ID をまとめて一括でインポートします。",
                FontSize = 12,
                Foreground = ThemeHelper.BrushTextMuted,
                Margin = new Thickness(0, 4, 0, 0)
            });
            Grid.SetRow(headerStack, 0);
            mainGrid.Children.Add(headerStack);

            // タブ制御
            _modeTabControl = new TabControl
            {
                Background = ThemeHelper.BrushBgDark,
                BorderBrush = ThemeHelper.BrushBorder,
                Foreground = ThemeHelper.BrushTextMain
            };

            var tabFiles = new TabItem { Header = "📁 鍵ファイル・フォルダー一括指定" };
            tabFiles.Content = CreateFilesTabContent();

            var tabText = new TabItem { Header = "📝 ASCII Armor テキスト一括貼り付け" };
            tabText.Content = CreateTextTabContent();

            var tabKeyserver = new TabItem { Header = "🌐 キーサーバーから一括取得" };
            tabKeyserver.Content = CreateKeyserverTabContent();

            _modeTabControl.Items.Add(tabFiles);
            _modeTabControl.Items.Add(tabText);
            _modeTabControl.Items.Add(tabKeyserver);

            _modeTabControl.SelectionChanged += (s, e) => UpdateOverallSummary();

            Grid.SetRow(_modeTabControl, 1);
            mainGrid.Children.Add(_modeTabControl);

            // アクションエリア
            var actionStack = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
            _lblStatusSummary = new TextBlock
            {
                Text = "準備完了: 追加したい項目を選択または入力してください。",
                Foreground = ThemeHelper.BrushTextMuted,
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 8),
                TextWrapping = TextWrapping.Wrap
            };
            actionStack.Children.Add(_lblStatusSummary);

            var btnPnl = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            _btnImport = ThemeHelper.CreatePrimaryButton("📥 複数鍵を一括インポート実行", async (s, e) => await OnImportClickAsync());
            var btnCancel = ThemeHelper.CreateSecondaryButton("閉じる", (s, e) => Close());
            btnCancel.IsCancel = true;

            btnPnl.Children.Add(btnCancel);
            btnPnl.Children.Add(_btnImport);
            actionStack.Children.Add(btnPnl);

            Grid.SetRow(actionStack, 2);
            mainGrid.Children.Add(actionStack);

            Content = mainGrid;

            UpdateOverallSummary();
        }

        private UIElement CreateFilesTabContent()
        {
            var grid = new Grid { Margin = new Thickness(12) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            var btnBrowseFiles = ThemeHelper.CreatePrimaryButton("📄 複数鍵ファイルを選択...", (s, e) => BrowseFiles());
            var btnBrowseFolder = ThemeHelper.CreateSecondaryButton("📁 鍵フォルダーを選択...", (s, e) => BrowseFolder());
            var btnClear = ThemeHelper.CreateSecondaryButton("🗑 リストをクリア", (s, e) => ClearFilesList());

            toolbar.Children.Add(btnBrowseFiles);
            toolbar.Children.Add(btnBrowseFolder);
            toolbar.Children.Add(btnClear);

            Grid.SetRow(toolbar, 0);
            grid.Children.Add(toolbar);

            _lstFileItems = new ListView
            {
                Background = ThemeHelper.BrushSurfaceDark,
                Foreground = ThemeHelper.BrushTextMain,
                BorderBrush = ThemeHelper.BrushBorder,
                BorderThickness = new Thickness(1),
                ItemsSource = _selectedFiles,
                MinHeight = 160
            };

            var gv = new GridView();
            gv.Columns.Add(new GridViewColumn { Header = "ファイル名", DisplayMemberBinding = new System.Windows.Data.Binding("FileName"), Width = 180 });
            gv.Columns.Add(new GridViewColumn { Header = "サイズ", DisplayMemberBinding = new System.Windows.Data.Binding("DisplaySize"), Width = 80 });
            gv.Columns.Add(new GridViewColumn { Header = "フルパス", DisplayMemberBinding = new System.Windows.Data.Binding("FullPath"), Width = 320 });

            _lstFileItems.View = gv;
            _lstFileItems.AllowDrop = true;
            _lstFileItems.PreviewDragOver += (s, e) => { e.Effects = DragDropEffects.Copy; e.Handled = true; };
            _lstFileItems.Drop += (s, e) => HandleDrop(e);

            var dropCard = ThemeHelper.CreateCardPanel(_lstFileItems, 0);
            Grid.SetRow(dropCard, 1);
            grid.Children.Add(dropCard);

            _lblFileStatus = new TextBlock
            {
                Text = "ここに複数の鍵ファイル (.asc, .pub, .gpg, .key) またはフォルダーをドラッグ＆ドロップできます。",
                Foreground = ThemeHelper.BrushTextMuted,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            };

            _txtFileResultDetail = new TextBlock
            {
                Text = "",
                Foreground = ThemeHelper.BrushTextMain,
                FontSize = 11,
                Margin = new Thickness(0, 6, 0, 0),
                TextWrapping = TextWrapping.Wrap,
                Visibility = Visibility.Collapsed
            };

            var fileStack = new StackPanel { Margin = new Thickness(10, 8, 10, 8) };
            fileStack.Children.Add(_lblFileStatus);
            fileStack.Children.Add(_txtFileResultDetail);

            _cardFileStatus = new Border
            {
                Child = fileStack,
                Background = ThemeHelper.BrushSurfaceDark,
                BorderBrush = ThemeHelper.BrushBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Margin = new Thickness(0, 8, 0, 0)
            };
            Grid.SetRow(_cardFileStatus, 2);
            grid.Children.Add(_cardFileStatus);

            return grid;
        }

        private UIElement CreateTextTabContent()
        {
            var grid = new Grid { Margin = new Thickness(12) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            var btnPaste = ThemeHelper.CreatePrimaryButton("📋 クリップボードから貼り付け", (s, e) =>
            {
                try
                {
                    if (Clipboard.ContainsText())
                    {
                        _txtArmorInput.Text = Clipboard.GetText();
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("クリップボードからの貼り付けに失敗しました: " + ex.Message, "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            });
            var btnLoadFile = ThemeHelper.CreateSecondaryButton("📄 テキストファイルから読み込み...", (s, e) =>
            {
                var ofd = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = "テキスト/鍵ファイル (*.txt;*.asc;*.pub;*.pgp;*.key)|*.txt;*.asc;*.pub;*.pgp;*.key|すべてのファイル (*.*)|*.*",
                    Title = "ASCII Armor テキストファイルの選択"
                };
                if (ofd.ShowDialog() == true && File.Exists(ofd.FileName))
                {
                    try
                    {
                        _txtArmorInput.Text = File.ReadAllText(ofd.FileName, Encoding.UTF8);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("ファイルの読み込みに失敗しました: " + ex.Message, "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            });
            var btnClear = ThemeHelper.CreateSecondaryButton("🗑 テキストクリア", (s, e) => _txtArmorInput.Text = "");

            toolbar.Children.Add(btnPaste);
            toolbar.Children.Add(btnLoadFile);
            toolbar.Children.Add(btnClear);

            Grid.SetRow(toolbar, 0);
            grid.Children.Add(toolbar);

            _txtArmorInput = ThemeHelper.CreateStyledTextBox();
            _txtArmorInput.AcceptsReturn = true;
            _txtArmorInput.AcceptsTab = true;
            _txtArmorInput.TextWrapping = TextWrapping.NoWrap; // 長い行やコードの折返し遅延を防止し高速描画
            _txtArmorInput.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            _txtArmorInput.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            _txtArmorInput.FontFamily = new FontFamily("Consolas");
            _txtArmorInput.FontSize = 12.5;
            _txtArmorInput.MinHeight = 320;
            _txtArmorInput.MaxLength = 0; // 0 は WPF で完全無制限 (制限なし)
            _txtArmorInput.UndoLimit = 0; // 5MBを超える大容量テキスト貼り付け時のメモリ・バッファオーバーヘッドを回避
            _txtArmorInput.VerticalContentAlignment = VerticalAlignment.Top;
            _txtArmorInput.HorizontalContentAlignment = HorizontalAlignment.Left;
            _txtArmorInput.Padding = new Thickness(8);
            _txtArmorInput.AllowDrop = true;
            _txtArmorInput.Drop += (s, e) => HandleDrop(e);
            _txtArmorInput.TextChanged += (s, e) => UpdateTextTabStatus();

            // Ctrl+V などの貼り付け操作で超大型テキストが途中で切り落とされないよう手動ペースト処理を追加
            DataObject.AddPastingHandler(_txtArmorInput, (s, e) =>
            {
                if (e.DataObject.GetDataPresent(DataFormats.UnicodeText))
                {
                    string pastedText = e.DataObject.GetData(DataFormats.UnicodeText) as string;
                    if (!string.IsNullOrEmpty(pastedText))
                    {
                        int selStart = _txtArmorInput.SelectionStart;
                        int selLen = _txtArmorInput.SelectionLength;
                        if (selLen > 0 || _txtArmorInput.Text.Length > 0)
                        {
                            _txtArmorInput.SelectedText = pastedText;
                        }
                        else
                        {
                            _txtArmorInput.Text = pastedText;
                        }
                        _txtArmorInput.SelectionStart = selStart + pastedText.Length;
                        _txtArmorInput.SelectionLength = 0;
                        e.CancelCommand();
                        e.Handled = true;
                    }
                }
            });

            var inputCard = new Border
            {
                Child = _txtArmorInput,
                BorderBrush = ThemeHelper.BrushBorder,
                BorderThickness = new Thickness(1)
            };
            Grid.SetRow(inputCard, 1);
            grid.Children.Add(inputCard);

            _lblArmorStatus = new TextBlock
            {
                Text = "複数鍵の ASCII Armor ブロック (-----BEGIN PGP PUBLIC KEY BLOCK-----) を複数まとめて貼り付け可能です。",
                Foreground = ThemeHelper.BrushTextMuted,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            };

            _txtArmorResultDetail = new TextBlock
            {
                Text = "",
                Foreground = ThemeHelper.BrushTextMain,
                FontSize = 11,
                Margin = new Thickness(0, 6, 0, 0),
                TextWrapping = TextWrapping.Wrap,
                Visibility = Visibility.Collapsed
            };

            var armorStack = new StackPanel { Margin = new Thickness(10, 8, 10, 8) };
            armorStack.Children.Add(_lblArmorStatus);
            armorStack.Children.Add(_txtArmorResultDetail);

            _cardArmorStatus = new Border
            {
                Child = armorStack,
                Background = ThemeHelper.BrushSurfaceDark,
                BorderBrush = ThemeHelper.BrushBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Margin = new Thickness(0, 8, 0, 0)
            };
            Grid.SetRow(_cardArmorStatus, 2);
            grid.Children.Add(_cardArmorStatus);

            return grid;
        }

        private UIElement CreateKeyserverTabContent()
        {
            var grid = new Grid { Margin = new Thickness(12) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var serverPnl = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            serverPnl.Children.Add(new TextBlock
            {
                Text = "キーサーバー:",
                Foreground = ThemeHelper.BrushTextMuted,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            });

            _cboKeyserver = new ComboBox
            {
                Height = 32,
                Width = 260,
                Background = ThemeHelper.BrushBgDark,
                Foreground = ThemeHelper.BrushTextMain,
                BorderBrush = ThemeHelper.BrushBorder,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            _cboKeyserver.Items.Add("hkps://keyserver.ubuntu.com");
            _cboKeyserver.Items.Add("hkps://keys.openpgp.org");
            _cboKeyserver.Items.Add("hkps://pgp.mit.edu");
            _cboKeyserver.SelectedIndex = 0;

            serverPnl.Children.Add(_cboKeyserver);

            Grid.SetRow(serverPnl, 0);
            grid.Children.Add(serverPnl);

            var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            var btnPaste = ThemeHelper.CreatePrimaryButton("📋 クリップボードから貼り付け", (s, e) =>
            {
                if (Clipboard.ContainsText())
                {
                    _txtKeyIdsInput.Text = Clipboard.GetText();
                }
            });
            var btnClear = ThemeHelper.CreateSecondaryButton("🗑 クリア", (s, e) => _txtKeyIdsInput.Text = "");

            toolbar.Children.Add(btnPaste);
            toolbar.Children.Add(btnClear);

            Grid.SetRow(toolbar, 1);
            grid.Children.Add(toolbar);

            _txtKeyIdsInput = ThemeHelper.CreateStyledTextBox();
            _txtKeyIdsInput.AcceptsReturn = true;
            _txtKeyIdsInput.TextWrapping = TextWrapping.Wrap;
            _txtKeyIdsInput.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            _txtKeyIdsInput.FontFamily = new FontFamily("Consolas");
            _txtKeyIdsInput.VerticalContentAlignment = VerticalAlignment.Top;
            _txtKeyIdsInput.HorizontalContentAlignment = HorizontalAlignment.Left;
            _txtKeyIdsInput.Padding = new Thickness(8);
            _txtKeyIdsInput.TextChanged += (s, e) => UpdateKeyserverTabStatus();

            var inputCard = new Border
            {
                Child = _txtKeyIdsInput,
                BorderBrush = ThemeHelper.BrushBorder,
                BorderThickness = new Thickness(1)
            };
            Grid.SetRow(inputCard, 2);
            grid.Children.Add(inputCard);

            _lblKeyIdsStatus = new TextBlock
            {
                Text = "複数のキーIDまたはフィンガープリントを改行またはカンマ・スペース区切りで入力してください。\n(例: 0x3B0EB3C212345678, 4A3B2C1D90876543)",
                Foreground = ThemeHelper.BrushTextMuted,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            };

            _txtKeyserverResultDetail = new TextBlock
            {
                Text = "",
                Foreground = ThemeHelper.BrushTextMain,
                FontSize = 11,
                Margin = new Thickness(0, 6, 0, 0),
                TextWrapping = TextWrapping.Wrap,
                Visibility = Visibility.Collapsed
            };

            var keyserverStack = new StackPanel { Margin = new Thickness(10, 8, 10, 8) };
            keyserverStack.Children.Add(_lblKeyIdsStatus);
            keyserverStack.Children.Add(_txtKeyserverResultDetail);

            _cardKeyserverStatus = new Border
            {
                Child = keyserverStack,
                Background = ThemeHelper.BrushSurfaceDark,
                BorderBrush = ThemeHelper.BrushBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Margin = new Thickness(0, 8, 0, 0)
            };
            Grid.SetRow(_cardKeyserverStatus, 3);
            grid.Children.Add(_cardKeyserverStatus);

            return grid;
        }

        private void BrowseFiles()
        {
            var ofd = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "GPG 鍵ファイル (*.asc;*.gpg;*.pub;*.sec;*.key)|*.asc;*.gpg;*.pub;*.sec;*.key|すべてのファイル (*.*)|*.*",
                Multiselect = true
            };

            if (ofd.ShowDialog() == true && ofd.FileNames != null && ofd.FileNames.Length > 0)
            {
                AddSelectedFiles(ofd.FileNames);
            }
        }

        private void BrowseFolder()
        {
            using (var fbd = new System.Windows.Forms.FolderBrowserDialog())
            {
                fbd.Description = "複数の鍵ファイルが含まれるフォルダーを選択してください。";
                fbd.ShowNewFolderButton = false;
                if (fbd.ShowDialog() == System.Windows.Forms.DialogResult.OK && !string.IsNullOrWhiteSpace(fbd.SelectedPath))
                {
                    AddSelectedFiles(new[] { fbd.SelectedPath });
                }
            }
        }

        private void AddSelectedFiles(IEnumerable<string> paths)
        {
            if (paths == null) return;

            var existingPaths = new HashSet<string>(_selectedFiles.Select(f => f.FullPath), StringComparer.OrdinalIgnoreCase);

            foreach (var p in paths)
            {
                if (File.Exists(p))
                {
                    if (!existingPaths.Contains(p))
                    {
                        var fi = new FileInfo(p);
                        _selectedFiles.Add(new KeyFileItem { FileName = fi.Name, FullPath = fi.FullName, Size = fi.Length });
                        existingPaths.Add(p);
                    }
                }
                else if (Directory.Exists(p))
                {
                    try
                    {
                        var found = Directory.GetFiles(p, "*.*", SearchOption.AllDirectories)
                            .Where(f => f.EndsWith(".asc", StringComparison.OrdinalIgnoreCase) ||
                                        f.EndsWith(".gpg", StringComparison.OrdinalIgnoreCase) ||
                                        f.EndsWith(".pub", StringComparison.OrdinalIgnoreCase) ||
                                        f.EndsWith(".sec", StringComparison.OrdinalIgnoreCase) ||
                                        f.EndsWith(".key", StringComparison.OrdinalIgnoreCase))
                            .ToList();

                        foreach (var f in found)
                        {
                            if (!existingPaths.Contains(f))
                            {
                                var fi = new FileInfo(f);
                                _selectedFiles.Add(new KeyFileItem { FileName = fi.Name, FullPath = fi.FullName, Size = fi.Length });
                                existingPaths.Add(f);
                            }
                        }
                    }
                    catch { }
                }
            }

            UpdateFilesTabStatus();
            UpdateOverallSummary();
        }

        private void ClearFilesList()
        {
            _selectedFiles.Clear();
            UpdateFilesTabStatus();
            UpdateOverallSummary();
        }

        private void UpdateFilesTabStatus()
        {
            if (_txtFileResultDetail != null)
            {
                _txtFileResultDetail.Visibility = Visibility.Collapsed;
                _txtFileResultDetail.Text = "";
            }

            if (_selectedFiles.Count == 0)
            {
                if (_cardFileStatus != null)
                {
                    _cardFileStatus.Background = ThemeHelper.BrushSurfaceDark;
                    _cardFileStatus.BorderBrush = ThemeHelper.BrushBorder;
                }
                _lblFileStatus.Foreground = ThemeHelper.BrushTextMuted;
                _lblFileStatus.Text = "ここに複数の鍵ファイル (.asc, .pub, .gpg, .key) またはフォルダーをドラッグ＆ドロップできます。";
            }
            else
            {
                if (_cardFileStatus != null)
                {
                    _cardFileStatus.Background = new SolidColorBrush(Color.FromArgb(40, 16, 185, 129));
                    _cardFileStatus.BorderBrush = ThemeHelper.BrushSuccessGreen;
                }
                _lblFileStatus.Foreground = ThemeHelper.BrushSuccessGreen;
                _lblFileStatus.Text = string.Format("✅ 読み取り成功: {0} 件の鍵ファイルが選択されています。", _selectedFiles.Count);
            }
        }

        private static int FastCountOccurrences(string text, string pattern)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(pattern)) return 0;
            int count = 0;
            int i = 0;
            while ((i = text.IndexOf(pattern, i, StringComparison.Ordinal)) != -1)
            {
                count++;
                i += pattern.Length;
            }
            return count;
        }

        private void UpdateTextTabStatus()
        {
            string text = _txtArmorInput != null ? _txtArmorInput.Text : "";
            int countPub = FastCountOccurrences(text, "-----BEGIN PGP PUBLIC KEY BLOCK-----");
            int countSec = FastCountOccurrences(text, "-----BEGIN PGP PRIVATE KEY BLOCK-----") + FastCountOccurrences(text, "-----BEGIN PGP SECRET KEY BLOCK-----");
            int total = countPub + countSec;

            if (_txtArmorResultDetail != null)
            {
                _txtArmorResultDetail.Visibility = Visibility.Collapsed;
                _txtArmorResultDetail.Text = "";
            }

            if (total > 0)
            {
                if (_cardArmorStatus != null)
                {
                    _cardArmorStatus.Background = new SolidColorBrush(Color.FromArgb(40, 16, 185, 129));
                    _cardArmorStatus.BorderBrush = ThemeHelper.BrushSuccessGreen;
                }
                _lblArmorStatus.Foreground = ThemeHelper.BrushSuccessGreen;
                _lblArmorStatus.Text = string.Format("✅ 読み取り成功: 有効な PGP ASCII Armor ブロックを検出しました (合計: {0} 件 - 公開鍵: {1} 件, 秘密鍵: {2} 件)", total, countPub, countSec);
            }
            else if (!string.IsNullOrWhiteSpace(text))
            {
                if (_cardArmorStatus != null)
                {
                    _cardArmorStatus.Background = new SolidColorBrush(Color.FromArgb(40, 239, 68, 68));
                    _cardArmorStatus.BorderBrush = ThemeHelper.BrushDangerRed;
                }
                _lblArmorStatus.Foreground = ThemeHelper.BrushDangerRed;
                _lblArmorStatus.Text = "❌ 読み取り失敗: 有効な PGP ASCII Armor ヘッダー (-----BEGIN PGP PUBLIC KEY BLOCK----- または -----BEGIN PGP PRIVATE KEY BLOCK-----) が検出できません。";
            }
            else
            {
                if (_cardArmorStatus != null)
                {
                    _cardArmorStatus.Background = ThemeHelper.BrushSurfaceDark;
                    _cardArmorStatus.BorderBrush = ThemeHelper.BrushBorder;
                }
                _lblArmorStatus.Foreground = ThemeHelper.BrushTextMuted;
                _lblArmorStatus.Text = "複数鍵の ASCII Armor ブロック (-----BEGIN PGP PUBLIC KEY BLOCK-----) を複数まとめて貼り付け可能です。";
            }

            UpdateOverallSummary();
        }

        private void UpdateKeyserverTabStatus()
        {
            string text = _txtKeyIdsInput != null ? _txtKeyIdsInput.Text : "";
            var matches = Regex.Matches(text, @"(?:0x)?[0-9a-fA-F]{8,40}");
            int count = matches.Count;

            if (_txtKeyserverResultDetail != null)
            {
                _txtKeyserverResultDetail.Visibility = Visibility.Collapsed;
                _txtKeyserverResultDetail.Text = "";
            }

            if (count > 0)
            {
                if (_cardKeyserverStatus != null)
                {
                    _cardKeyserverStatus.Background = new SolidColorBrush(Color.FromArgb(40, 16, 185, 129));
                    _cardKeyserverStatus.BorderBrush = ThemeHelper.BrushSuccessGreen;
                }
                _lblKeyIdsStatus.Foreground = ThemeHelper.BrushSuccessGreen;
                _lblKeyIdsStatus.Text = string.Format("✅ 読み取り成功: {0} 件のキーID / フィンガープリントが検出されました。", count);
            }
            else if (!string.IsNullOrWhiteSpace(text))
            {
                if (_cardKeyserverStatus != null)
                {
                    _cardKeyserverStatus.Background = new SolidColorBrush(Color.FromArgb(40, 239, 68, 68));
                    _cardKeyserverStatus.BorderBrush = ThemeHelper.BrushDangerRed;
                }
                _lblKeyIdsStatus.Foreground = ThemeHelper.BrushDangerRed;
                _lblKeyIdsStatus.Text = "❌ 読み取り失敗: 有効な 16進数キーID / フィンガープリントが検出されません。";
            }
            else
            {
                if (_cardKeyserverStatus != null)
                {
                    _cardKeyserverStatus.Background = ThemeHelper.BrushSurfaceDark;
                    _cardKeyserverStatus.BorderBrush = ThemeHelper.BrushBorder;
                }
                _lblKeyIdsStatus.Foreground = ThemeHelper.BrushTextMuted;
                _lblKeyIdsStatus.Text = "複数のキーIDまたはフィンガープリントを改行またはカンマ・スペース区切りで入力してください。";
            }

            UpdateOverallSummary();
        }

        private void UpdateOverallSummary()
        {
            if (_lblStatusSummary == null || _modeTabControl == null) return;

            int activeTab = _modeTabControl.SelectedIndex;
            if (activeTab == 0) // ファイル・フォルダー
            {
                if (_selectedFiles.Count > 0)
                {
                    _lblStatusSummary.Foreground = ThemeHelper.BrushSuccessGreen;
                    _lblStatusSummary.Text = string.Format("実行準備完了: 選択された {0} 件の鍵ファイルを一括インポートします。", _selectedFiles.Count);
                }
                else
                {
                    _lblStatusSummary.Foreground = ThemeHelper.BrushTextMuted;
                    _lblStatusSummary.Text = "「ファイルを選択」ボタンまたはドロップで鍵ファイルを複数指定してください。";
                }
            }
            else if (activeTab == 1) // テキスト
            {
                string text = _txtArmorInput != null ? _txtArmorInput.Text.Trim() : "";
                if (!string.IsNullOrEmpty(text))
                {
                    int total = Regex.Matches(text, @"-----BEGIN PGP").Count;
                    _lblStatusSummary.Foreground = ThemeHelper.BrushSuccessGreen;
                    _lblStatusSummary.Text = string.Format("実行準備完了: 入力されたテキスト ({0} ブロック検出) をインポートします。", total > 0 ? total.ToString() + " 件" : "未パース");
                }
                else
                {
                    _lblStatusSummary.Foreground = ThemeHelper.BrushTextMuted;
                    _lblStatusSummary.Text = "ASCII Armor テキストを貼り付けてください。";
                }
            }
            else if (activeTab == 2) // キーサーバー
            {
                string text = _txtKeyIdsInput != null ? _txtKeyIdsInput.Text.Trim() : "";
                var matches = Regex.Matches(text, @"(?:0x)?[0-9a-fA-F]{8,40}");
                if (matches.Count > 0)
                {
                    _lblStatusSummary.Foreground = ThemeHelper.BrushSuccessGreen;
                    _lblStatusSummary.Text = string.Format("実行準備完了: キーサーバーから {0} 件のキーIDを一括取得します。", matches.Count);
                }
                else
                {
                    _lblStatusSummary.Foreground = ThemeHelper.BrushTextMuted;
                    _lblStatusSummary.Text = "取得したいキーIDを入力してください。";
                }
            }
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            HandleDrop(e);
        }

        private void HandleDrop(DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files != null && files.Length > 0)
                {
                    if (_modeTabControl != null) _modeTabControl.SelectedIndex = 0;
                    AddSelectedFiles(files);
                    e.Handled = true;
                }
            }
        }

        private async Task OnImportClickAsync()
        {
            int activeTab = _modeTabControl.SelectedIndex;
            CryptoResult res = null;

            _btnImport.IsEnabled = false;
            _lblStatusSummary.Foreground = ThemeHelper.BrushPrimaryCyan;
            _lblStatusSummary.Text = "⏳ 鍵をインポート処理中 (0 件処理完了)...";

            int processedCount = 0;
            int importedCount = 0;
            int unchangedCount = 0;

            Action<string> onLineReceived = (line) =>
            {
                if (string.IsNullOrEmpty(line)) return;

                bool updated = false;

                // [GNUPG:] 機械用ステータスタグの解析
                if (line.StartsWith("[GNUPG:]"))
                {
                    // 1) 機械判別用ステータス [GNUPG:] IMPORT_OK <reason_flag> <fingerprint>
                    var matchOk = Regex.Match(line, @"\[GNUPG:\]\s+IMPORT_OK\s+(\d+)", RegexOptions.IgnoreCase);
                    if (matchOk.Success)
                    {
                        processedCount++;
                        int reasonFlag = 0;
                        int.TryParse(matchOk.Groups[1].Value, out reasonFlag);
                        if (reasonFlag == 0) unchangedCount++;
                        else importedCount++;
                        updated = true;
                    }
                    // 2) 機械判別用ステータス [GNUPG:] IMPORT_RES <count> ...
                    else if (line.Contains("IMPORT_RES"))
                    {
                        var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 7)
                        {
                            int t, imp, unc;
                            if (int.TryParse(parts[2], out t)) processedCount = Math.Max(processedCount, t);
                            if (int.TryParse(parts[4], out imp)) importedCount = Math.Max(importedCount, imp);
                            if (int.TryParse(parts[6], out unc)) unchangedCount = Math.Max(unchangedCount, unc);
                            updated = true;
                        }
                    }
                }
                else
                {
                    // 人間用ログメッセージまたは標準集計ログ（[GNUPG:] タグがない場合）
                    var matchTotal = Regex.Match(line, @"(?:Total number processed|処理数の合計)\s*:\s*(\d+)", RegexOptions.IgnoreCase);
                    if (matchTotal.Success)
                    {
                        int totalVal;
                        if (int.TryParse(matchTotal.Groups[1].Value, out totalVal)) processedCount = Math.Max(processedCount, totalVal);
                        updated = true;
                    }
                    else if (processedCount == 0)
                    {
                        // IMPORT_OK が出力されない場合のみ代替カウント
                        if (Regex.IsMatch(line, @"(?:key|鍵)\s+[0-9a-fA-F]+:", RegexOptions.IgnoreCase) ||
                            Regex.IsMatch(line, @"imported|not changed|new subkeys|インポート|変更なし", RegexOptions.IgnoreCase))
                        {
                            processedCount++;
                            if (Regex.IsMatch(line, @"imported|new subkeys|インポート", RegexOptions.IgnoreCase)) importedCount++;
                            else if (Regex.IsMatch(line, @"not changed|変更なし", RegexOptions.IgnoreCase)) unchangedCount++;
                            updated = true;
                        }
                    }
                }

                if (updated)
                {
                    int p = processedCount;
                    int imp = importedCount;
                    int unc = unchangedCount;
                    Dispatcher.InvokeAsync(() =>
                    {
                        if (p > 0)
                        {
                            _lblStatusSummary.Text = string.Format("⏳ 鍵をインポート処理中... (進行状況: {0} 件 処理完了 - 新規: {1} 件, 既存: {2} 件)", p, imp, unc);
                        }
                    });
                }
            };

            try
            {
                if (activeTab == 0) // ファイル一括
                {
                    if (_selectedFiles.Count == 0)
                    {
                        if (_cardFileStatus != null)
                        {
                            _cardFileStatus.Background = new SolidColorBrush(Color.FromArgb(40, 239, 68, 68));
                            _cardFileStatus.BorderBrush = ThemeHelper.BrushDangerRed;
                        }
                        _lblFileStatus.Foreground = ThemeHelper.BrushDangerRed;
                        _lblFileStatus.Text = "❌ エラー: インポートする鍵ファイルを選択してください。";
                        _lblStatusSummary.Foreground = ThemeHelper.BrushDangerRed;
                        _lblStatusSummary.Text = "❌ 鍵ファイルが選択されていません。";
                        _btnImport.IsEnabled = true;
                        return;
                    }

                    var paths = _selectedFiles.Select(f => f.FullPath).ToList();
                    res = await _gpgService.ImportKeyFilesAsync(paths, onLineReceived);
                }
                else if (activeTab == 1) // テキスト一括
                {
                    string input = _txtArmorInput != null ? _txtArmorInput.Text.Trim() : "";
                    if (string.IsNullOrEmpty(input))
                    {
                        if (_cardArmorStatus != null)
                        {
                            _cardArmorStatus.Background = new SolidColorBrush(Color.FromArgb(40, 239, 68, 68));
                            _cardArmorStatus.BorderBrush = ThemeHelper.BrushDangerRed;
                        }
                        _lblArmorStatus.Foreground = ThemeHelper.BrushDangerRed;
                        _lblArmorStatus.Text = "❌ エラー: 鍵の ASCII Armor テキストを入力してください。";
                        _lblStatusSummary.Foreground = ThemeHelper.BrushDangerRed;
                        _lblStatusSummary.Text = "❌ ASCII Armor テキストが未入力です。";
                        _btnImport.IsEnabled = true;
                        return;
                    }

                    res = await _gpgService.ImportKeyAsync(input, onLineReceived);
                }
                else if (activeTab == 2) // キーサーバー
                {
                    string input = _txtKeyIdsInput != null ? _txtKeyIdsInput.Text.Trim() : "";
                    var matches = Regex.Matches(input, @"(?:0x)?[0-9a-fA-F]{8,40}");
                    var keyIds = matches.Cast<Match>().Select(m => m.Value).Distinct().ToList();

                    if (keyIds.Count == 0)
                    {
                        if (_cardKeyserverStatus != null)
                        {
                            _cardKeyserverStatus.Background = new SolidColorBrush(Color.FromArgb(40, 239, 68, 68));
                            _cardKeyserverStatus.BorderBrush = ThemeHelper.BrushDangerRed;
                        }
                        _lblKeyIdsStatus.Foreground = ThemeHelper.BrushDangerRed;
                        _lblKeyIdsStatus.Text = "❌ エラー: 有効なキーIDまたはフィンガープリントを入力してください。";
                        _lblStatusSummary.Foreground = ThemeHelper.BrushDangerRed;
                        _lblStatusSummary.Text = "❌ キーIDが未入力です。";
                        _btnImport.IsEnabled = true;
                        return;
                    }

                    string server = _cboKeyserver.SelectedItem != null ? _cboKeyserver.SelectedItem.ToString() : "hkps://keyserver.ubuntu.com";
                    res = await _gpgService.ReceiveKeysAsync(keyIds, server, onLineReceived);
                }

                if (res != null)
                {
                    var summary = GpgService.ParseImportSummary(res.OutputText, res.Success);

                    if (res.Success)
                    {
                        IsKeyImported = true;

                        if (activeTab == 1) // ASCII Armor テキスト
                        {
                            _cardArmorStatus.Background = new SolidColorBrush(Color.FromArgb(50, 16, 185, 129));
                            _cardArmorStatus.BorderBrush = ThemeHelper.BrushSuccessGreen;
                            _lblArmorStatus.Foreground = ThemeHelper.BrushSuccessGreen;
                            _lblArmorStatus.Text = string.Format("🎉 インポート成功: {0} 件の鍵を正常に取り込みました！ (新規追加: {1} 件, 既存/更新: {2} 件)", 
                                summary.TotalProcessed > 0 ? summary.TotalProcessed : (summary.ImportedCount + summary.UnchangedCount),
                                summary.ImportedCount, summary.UnchangedCount);

                            if (summary.ImportedKeyDetails.Count > 0)
                            {
                                _txtArmorResultDetail.Text = "【取り込まれた鍵のユーザー詳細】\n• " + string.Join("\n• ", summary.ImportedKeyDetails);
                                _txtArmorResultDetail.Visibility = Visibility.Visible;
                            }
                        }
                        else if (activeTab == 0) // ファイル
                        {
                            _cardFileStatus.Background = new SolidColorBrush(Color.FromArgb(50, 16, 185, 129));
                            _cardFileStatus.BorderBrush = ThemeHelper.BrushSuccessGreen;
                            _lblFileStatus.Foreground = ThemeHelper.BrushSuccessGreen;
                            _lblFileStatus.Text = string.Format("🎉 インポート成功: {0} 件のファイルから鍵を取り込みました！ (新規: {1} 件, 既存: {2} 件)", _selectedFiles.Count, summary.ImportedCount, summary.UnchangedCount);

                            if (summary.ImportedKeyDetails.Count > 0)
                            {
                                _txtFileResultDetail.Text = "【取り込まれた鍵のユーザー詳細】\n• " + string.Join("\n• ", summary.ImportedKeyDetails);
                                _txtFileResultDetail.Visibility = Visibility.Visible;
                            }
                        }
                        else if (activeTab == 2) // キーサーバー
                        {
                            _cardKeyserverStatus.Background = new SolidColorBrush(Color.FromArgb(50, 16, 185, 129));
                            _cardKeyserverStatus.BorderBrush = ThemeHelper.BrushSuccessGreen;
                            _lblKeyIdsStatus.Foreground = ThemeHelper.BrushSuccessGreen;
                            _lblKeyIdsStatus.Text = string.Format("🎉 キーサーバー取得・インポート成功 (新規: {0} 件, 既存: {1} 件)", summary.ImportedCount, summary.UnchangedCount);

                            if (summary.ImportedKeyDetails.Count > 0)
                            {
                                _txtKeyserverResultDetail.Text = "【取り込まれた鍵のユーザー詳細】\n• " + string.Join("\n• ", summary.ImportedKeyDetails);
                                _txtKeyserverResultDetail.Visibility = Visibility.Visible;
                            }
                        }

                        _lblStatusSummary.Foreground = ThemeHelper.BrushSuccessGreen;
                        _lblStatusSummary.Text = string.Format("✅ インポート成功 (新規追加: {0} 件, 既存/更新: {1} 件)", summary.ImportedCount, summary.UnchangedCount);
                        _btnImport.IsEnabled = true;
                        _btnImport.Content = "📥 複数鍵を一括インポート実行";
                    }
                    else
                    {
                        bool isTimeout = res.ErrorMessage.Contains("タイムアウト") || res.ErrorMessage.Contains("timeout");
                        string failTitle = isTimeout 
                            ? string.Format("⏱️ タイムアウト: 制限時間内に処理が完了しませんでした ({0} 件処理完了時点で中断)。データ量が非常に多いか応答が停止した可能性があります。", processedCount)
                            : "❌ インポート失敗: " + res.ErrorMessage;

                        if (activeTab == 1)
                        {
                            _cardArmorStatus.Background = new SolidColorBrush(Color.FromArgb(50, 239, 68, 68));
                            _cardArmorStatus.BorderBrush = ThemeHelper.BrushDangerRed;
                            _lblArmorStatus.Foreground = ThemeHelper.BrushDangerRed;
                            _lblArmorStatus.Text = failTitle;
                        }
                        else if (activeTab == 0)
                        {
                            _cardFileStatus.Background = new SolidColorBrush(Color.FromArgb(50, 239, 68, 68));
                            _cardFileStatus.BorderBrush = ThemeHelper.BrushDangerRed;
                            _lblFileStatus.Foreground = ThemeHelper.BrushDangerRed;
                            _lblFileStatus.Text = failTitle;
                        }
                        else if (activeTab == 2)
                        {
                            _cardKeyserverStatus.Background = new SolidColorBrush(Color.FromArgb(50, 239, 68, 68));
                            _cardKeyserverStatus.BorderBrush = ThemeHelper.BrushDangerRed;
                            _lblKeyIdsStatus.Foreground = ThemeHelper.BrushDangerRed;
                            _lblKeyIdsStatus.Text = failTitle;
                        }

                        _lblStatusSummary.Foreground = ThemeHelper.BrushDangerRed;
                        _lblStatusSummary.Text = failTitle;
                        _btnImport.IsEnabled = true;
                    }
                }
            }
            catch (Exception ex)
            {
                _lblStatusSummary.Foreground = ThemeHelper.BrushDangerRed;
                _lblStatusSummary.Text = "エラー: " + ex.Message;
                if (_cardArmorStatus != null && activeTab == 1)
                {
                    _cardArmorStatus.Background = new SolidColorBrush(Color.FromArgb(50, 239, 68, 68));
                    _cardArmorStatus.BorderBrush = ThemeHelper.BrushDangerRed;
                    _lblArmorStatus.Foreground = ThemeHelper.BrushDangerRed;
                    _lblArmorStatus.Text = "❌ インポート例外エラー: " + ex.Message;
                }
                _btnImport.IsEnabled = true;
            }
        }

        private void ShowImportSuccessResultDialog(GpgImportSummary summary, string rawOutput)
        {
            var dlg = new Window
            {
                Title = "鍵インポート完了結果サマリー",
                Width = 620,
                Height = 480,
                MinWidth = 500,
                MinHeight = 380,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = ThemeHelper.BrushBgDark
            };

            var mainGrid = new Grid { Margin = new Thickness(20) };
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Stats
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Section Label
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // List / Raw Log
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Footer Button

            // Header
            var headerText = new TextBlock
            {
                Text = "🎉 鍵の取り込みが正常に完了しました！",
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Foreground = ThemeHelper.BrushSuccessGreen,
                Margin = new Thickness(0, 0, 0, 12)
            };
            Grid.SetRow(headerText, 0);
            mainGrid.Children.Add(headerText);

            // Stats
            var statsGrid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var statBox1 = new StackPanel();
            statBox1.Children.Add(new TextBlock { Text = string.Format("処理対象: {0} 件", summary.TotalProcessed > 0 ? summary.TotalProcessed : (summary.ImportedCount + summary.UnchangedCount)), Foreground = ThemeHelper.BrushTextMain, FontSize = 14, FontWeight = FontWeights.SemiBold });
            statBox1.Children.Add(new TextBlock { Text = string.Format("新規追加: {0} 件", summary.ImportedCount), Foreground = ThemeHelper.BrushPrimaryCyan, FontSize = 13 });
            Grid.SetColumn(statBox1, 0);
            statsGrid.Children.Add(statBox1);

            var statBox2 = new StackPanel();
            statBox2.Children.Add(new TextBlock { Text = string.Format("変更なし/更新: {0} 件", summary.UnchangedCount), Foreground = ThemeHelper.BrushTextMuted, FontSize = 14 });
            if (summary.SecretImportedCount > 0)
            {
                statBox2.Children.Add(new TextBlock { Text = string.Format("秘密鍵追加: {0} 件", summary.SecretImportedCount), Foreground = ThemeHelper.BrushAccentViolet, FontSize = 13 });
            }
            Grid.SetColumn(statBox2, 1);
            statsGrid.Children.Add(statBox2);

            var statsCard = ThemeHelper.CreateCardPanel(statsGrid, 12);
            Grid.SetRow(statsCard, 1);
            mainGrid.Children.Add(statsCard);

            // Section Label & Content
            if (summary.ImportedKeyDetails.Count > 0)
            {
                var lblDetails = new TextBlock
                {
                    Text = string.Format("取り込まれた鍵のユーザー詳細 ({0} 件):", summary.ImportedKeyDetails.Count),
                    Foreground = ThemeHelper.BrushTextMain,
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 13,
                    Margin = new Thickness(0, 8, 0, 4)
                };
                Grid.SetRow(lblDetails, 2);
                mainGrid.Children.Add(lblDetails);

                var lstDetails = new ListBox
                {
                    Background = ThemeHelper.BrushSurfaceDark,
                    Foreground = ThemeHelper.BrushTextMain,
                    BorderBrush = ThemeHelper.BrushBorder,
                    Margin = new Thickness(0, 0, 0, 12)
                };
                ScrollViewer.SetVerticalScrollBarVisibility(lstDetails, ScrollBarVisibility.Auto);
                ScrollViewer.SetHorizontalScrollBarVisibility(lstDetails, ScrollBarVisibility.Auto);

                // UIの仮想化を設定し、大量件数でもメモリ消費とレスポンスを最適化
                VirtualizingPanel.SetIsVirtualizing(lstDetails, true);
                VirtualizingPanel.SetVirtualizationMode(lstDetails, VirtualizationMode.Recycling);

                foreach (var detail in summary.ImportedKeyDetails)
                {
                    lstDetails.Items.Add(detail);
                }
                Grid.SetRow(lstDetails, 3);
                mainGrid.Children.Add(lstDetails);
            }
            else
            {
                var lblLog = new TextBlock
                {
                    Text = "詳細ログ (GnuPG 出力):",
                    Foreground = ThemeHelper.BrushTextMuted,
                    FontSize = 12,
                    Margin = new Thickness(0, 8, 0, 4)
                };
                Grid.SetRow(lblLog, 2);
                mainGrid.Children.Add(lblLog);

                var txtRaw = ThemeHelper.CreateStyledTextBox(GpgService.FilterStatusTags(rawOutput), true);
                txtRaw.AcceptsReturn = true;
                txtRaw.TextWrapping = TextWrapping.Wrap;
                txtRaw.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
                txtRaw.FontFamily = new FontFamily("Consolas");
                txtRaw.Margin = new Thickness(0, 0, 0, 12);

                Grid.SetRow(txtRaw, 3);
                mainGrid.Children.Add(txtRaw);
            }

            var btnOk = ThemeHelper.CreatePrimaryButton("確認", (s, e) => dlg.Close());
            btnOk.HorizontalAlignment = HorizontalAlignment.Right;
            btnOk.Margin = new Thickness(0, 4, 0, 0);
            Grid.SetRow(btnOk, 4);
            mainGrid.Children.Add(btnOk);

            dlg.Content = mainGrid;
            dlg.ShowDialog();
        }
    }

    public class KeyFileItem
    {
        public string FileName { get; set; }
        public string FullPath { get; set; }
        public long Size { get; set; }

        public string DisplaySize
        {
            get
            {
                if (Size < 1024) return Size + " B";
                if (Size < 1024 * 1024) return (Size / 1024.0).ToString("0.0") + " KB";
                return (Size / (1024.0 * 1024.0)).ToString("0.0") + " MB";
            }
        }
    }
}
