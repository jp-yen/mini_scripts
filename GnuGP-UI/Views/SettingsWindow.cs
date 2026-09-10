using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GpgUi.Views
{
    public class SettingsWindow : Window
    {
        private readonly GpgService _gpgService;

        private TextBox _txtGpgPath;
        private TextBlock _lblTestResult;
        private Button _btnTest;
        private Button _btnSave;

        public bool IsSettingsChanged { get; private set; }

        public SettingsWindow(GpgService gpgService)
        {
            _gpgService = gpgService;
            IsSettingsChanged = false;

            Title = "GnuPG 設定 - 実行ファイルパスの指定";
            Width = 580;
            Height = 360;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = ThemeHelper.BrushBgDark;
            ResizeMode = ResizeMode.NoResize;

            InitializeUI();
            ThemeHelper.ApplyDarkTitleBar(this);
        }

        private void InitializeUI()
        {
            var mainGrid = new Grid { Margin = new Thickness(20) };
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var header = new TextBlock
            {
                Text = "⚙️ GnuPG システム設定",
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                Foreground = ThemeHelper.BrushTextMain,
                Margin = new Thickness(0, 0, 0, 16)
            };
            Grid.SetRow(header, 0);
            mainGrid.Children.Add(header);

            var pathPnl = new StackPanel();
            pathPnl.Children.Add(new TextBlock
            {
                Text = "GnuPG 実行ファイル (gpg.exe) のパス:",
                FontSize = 13,
                Foreground = ThemeHelper.BrushTextMain,
                Margin = new Thickness(0, 0, 0, 8)
            });

            var pathGrid = new Grid();
            pathGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            pathGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _txtGpgPath = ThemeHelper.CreateStyledTextBox(_gpgService.GpgPath);
            Grid.SetColumn(_txtGpgPath, 0);
            pathGrid.Children.Add(_txtGpgPath);

            var btnBrowse = ThemeHelper.CreateSecondaryButton("参照 (Browse)...", (s, e) => BrowseGpgPath());
            Grid.SetColumn(btnBrowse, 1);
            pathGrid.Children.Add(btnBrowse);

            pathPnl.Children.Add(pathGrid);

            pathPnl.Children.Add(new TextBlock
            {
                Text = "※ デフォルトの 'gpg' のままの場合は環境変数 PATH の gpg.exe が使用されます。\n特定の GnuPG インストール先 (例: C:\\Program Files\\GnuPG\\bin\\gpg.exe) を直接指定することも可能です。",
                FontSize = 11,
                Foreground = ThemeHelper.BrushTextMuted,
                Margin = new Thickness(0, 8, 0, 0),
                TextWrapping = TextWrapping.Wrap
            });

            var card = ThemeHelper.CreateCardPanel(pathPnl, 14);
            Grid.SetRow(card, 1);
            mainGrid.Children.Add(card);

            _lblTestResult = new TextBlock
            {
                Text = "",
                FontSize = 12,
                Foreground = ThemeHelper.BrushSuccessGreen,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 10, 0, 10)
            };
            Grid.SetRow(_lblTestResult, 2);
            mainGrid.Children.Add(_lblTestResult);

            var actionStack = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };

            _btnTest = ThemeHelper.CreateSecondaryButton("接続テスト", async (s, e) => await TestPathAsync());
            _btnSave = ThemeHelper.CreatePrimaryButton("設定を保存", (s, e) => SaveSettings());
            var btnCancel = ThemeHelper.CreateSecondaryButton("キャンセル", (s, e) => Close());
            btnCancel.IsCancel = true;

            actionStack.Children.Add(_btnTest);
            actionStack.Children.Add(btnCancel);
            actionStack.Children.Add(_btnSave);

            Grid.SetRow(actionStack, 3);
            mainGrid.Children.Add(actionStack);

            Content = mainGrid;
        }

        private void BrowseGpgPath()
        {
            var ofd = new Microsoft.Win32.OpenFileDialog
            {
                Title = "GnuPG 実行ファイル (gpg.exe) を選択",
                Filter = "GnuPG 実行ファイル (gpg.exe)|gpg.exe|すべての実行ファイル (*.exe)|*.exe|すべてのファイル (*.*)|*.*"
            };
            if (ofd.ShowDialog() == true)
            {
                _txtGpgPath.Text = ofd.FileName;
            }
        }

        private async Task TestPathAsync()
        {
            string testPath = _txtGpgPath.Text.Trim();
            if (string.IsNullOrEmpty(testPath)) testPath = "gpg";

            _lblTestResult.Foreground = ThemeHelper.BrushPrimaryCyan;
            _lblTestResult.Text = "接続テスト実行中...";
            _btnTest.IsEnabled = false;

            var testService = new GpgService(testPath);
            var info = await testService.GetSystemInfoAsync();

            _btnTest.IsEnabled = true;
            if (info.IsAvailable)
            {
                _lblTestResult.Foreground = ThemeHelper.BrushSuccessGreen;
                _lblTestResult.Text = "✓ 接続成功: " + info.Version;
            }
            else
            {
                _lblTestResult.Foreground = ThemeHelper.BrushDangerRed;
                _lblTestResult.Text = "✗ 接続失敗: GnuPG 実行ファイルを検出できませんでした。\nパスを確認してください。";
            }
        }

        private void SaveSettings()
        {
            string newPath = _txtGpgPath.Text.Trim();
            if (string.IsNullOrEmpty(newPath)) newPath = "gpg";

            GpgService.SaveConfigPath(newPath);
            _gpgService.GpgPath = newPath;
            IsSettingsChanged = true;

            MessageBox.Show("GnuPG のパス設定を保存しました！", "設定保存", MessageBoxButton.OK, MessageBoxImage.Information);
            Close();
        }
    }
}
