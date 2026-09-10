using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GpgUi.Views;

namespace GpgUi
{
    public class MainWindow : Window
    {
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int attributeValue, int attributeValueSize);

        private readonly GpgService _gpgService;

        private DashboardView _dashboardView;
        private KeyManagerView _keyManagerView;
        private CryptoView _cryptoView;
        private SignVerifyView _signVerifyView;

        private ContentControl _contentArea;
        private Button _btnNavDashboard;
        private Button _btnNavKeys;
        private Button _btnNavCrypto;
        private Button _btnNavSignVerify;

        public MainWindow() : this(null)
        {
        }

        public MainWindow(string startupFilePath)
        {
            _gpgService = new GpgService();

            Title = "GnuPG UI - 鍵管理・暗号化ツール";
            Width = 1000;
            Height = 650;
            MinHeight = 450;
            MinWidth = 650;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = ThemeHelper.BrushBgDark;
            AllowDrop = true;

            InitializeUI();
            ThemeHelper.ApplyDarkTitleBar(this);

            Loaded += async (s, e) =>
            {
                await CheckGpgAvailabilityAsync();

                if (!string.IsNullOrEmpty(startupFilePath) && (System.IO.File.Exists(startupFilePath) || System.IO.Directory.Exists(startupFilePath)))
                {
                    NavigateTo("Crypto");
                    await _cryptoView.OpenFileSmartAsync(startupFilePath);
                }
            };
        }

        public async Task<bool> CheckGpgAvailabilityAsync()
        {
            var sysInfo = await _gpgService.GetSystemInfoAsync();
            if (sysInfo == null || !sysInfo.IsAvailable)
            {
                MessageBox.Show("gpg.exe 実行ファイルが見つかりません。\n設定画面で gpg.exe のファイルパスを選択してください。", "GnuPG 未検出", MessageBoxButton.OK, MessageBoxImage.Warning);
                OpenSettingsModal();
                return false;
            }
            return true;
        }

        private void InitializeUI()
        {
            var rootGrid = new Grid();
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var bodyGrid = new Grid();
            bodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
            bodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var sidebarBorder = new Border
            {
                Background = ThemeHelper.BrushSurfaceDark,
                BorderBrush = ThemeHelper.BrushBorder,
                BorderThickness = new Thickness(0, 0, 1, 0),
                Padding = new Thickness(12)
            };

            var sidebarStack = new StackPanel();

            var appTitle = new TextBlock
            {
                Text = "🔐 GnuPG UI",
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Foreground = ThemeHelper.BrushPrimaryCyan,
                Margin = new Thickness(4, 4, 0, 16)
            };
            sidebarStack.Children.Add(appTitle);

            _btnNavDashboard = CreateNavButton("📊  ダッシュボード", (s, e) => NavigateTo("Dashboard"));
            _btnNavKeys = CreateNavButton("🔑  鍵保管庫", (s, e) => NavigateTo("Keys"));
            _btnNavCrypto = CreateNavButton("🔐  暗号化・復号化", (s, e) => NavigateTo("Crypto"));
            _btnNavSignVerify = CreateNavButton("✒️  署名・検証", (s, e) => NavigateTo("SignVerify"));

            sidebarStack.Children.Add(_btnNavDashboard);
            sidebarStack.Children.Add(_btnNavKeys);
            sidebarStack.Children.Add(_btnNavCrypto);
            sidebarStack.Children.Add(_btnNavSignVerify);

            sidebarStack.Children.Add(new Border
            {
                Height = 1,
                Background = ThemeHelper.BrushBorder,
                Margin = new Thickness(0, 16, 0, 16)
            });

            var btnSettings = CreateNavButton("⚙️  設定", (s, e) => OpenSettingsModal());
            sidebarStack.Children.Add(btnSettings);

            sidebarBorder.Child = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = sidebarStack
            };
            Grid.SetColumn(sidebarBorder, 0);
            bodyGrid.Children.Add(sidebarBorder);

            _contentArea = new ContentControl();
            Grid.SetColumn(_contentArea, 1);
            bodyGrid.Children.Add(_contentArea);

            Grid.SetRow(bodyGrid, 0);
            rootGrid.Children.Add(bodyGrid);

            Content = rootGrid;

            _dashboardView = new DashboardView(_gpgService);
            _keyManagerView = new KeyManagerView(_gpgService, OpenGeneratorModal, OpenImportModal);
            _cryptoView = new CryptoView(_gpgService);
            _signVerifyView = new SignVerifyView(_gpgService);

            NavigateTo("Dashboard");
        }

        private Button CreateNavButton(string label, RoutedEventHandler onClick)
        {
            var btn = new Button
            {
                Content = label,
                // TryFindResource to avoid throwing if theme failed to load
                Style = TryFindResource("NavButtonStyle") as Style
            };
            if (onClick != null) btn.Click += onClick;
            return btn;
        }

        private void SetNavActive(Button activeBtn)
        {
            Button[] navs = { _btnNavDashboard, _btnNavKeys, _btnNavCrypto, _btnNavSignVerify };
            foreach (var b in navs)
            {
                b.Tag = null;
            }
            activeBtn.Tag = "Active";
        }

        private void ApplyDarkTitleBar()
        {
            try
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                if (hwnd == IntPtr.Zero) return;

                int useDarkMode = 1;
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDarkMode, sizeof(int));
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref useDarkMode, sizeof(int));
            }
            catch
            {
                // 失敗しても通常のタイトルバーのまま継続する
            }
        }

        public void NavigateTo(string viewName)
        {
            switch (viewName)
            {
                case "Dashboard":
                    _contentArea.Content = _dashboardView;
                    SetNavActive(_btnNavDashboard);
                    RunSafe(_dashboardView.RefreshDashboardAsync());
                    break;
                case "Keys":
                    _contentArea.Content = _keyManagerView;
                    SetNavActive(_btnNavKeys);
                    RunSafe(_keyManagerView.RefreshKeysAsync());
                    break;
                case "Crypto":
                    _contentArea.Content = _cryptoView;
                    SetNavActive(_btnNavCrypto);
                    RunSafe(_cryptoView.RefreshKeysAsync());
                    break;
                case "SignVerify":
                    _contentArea.Content = _signVerifyView;
                    SetNavActive(_btnNavSignVerify);
                    RunSafe(_signVerifyView.RefreshKeysAsync());
                    break;
            }
        }

        private void OpenGeneratorModal()
        {
            var modal = new KeyGeneratorWindow(_gpgService) { Owner = this };
            modal.ShowDialog();
            if (modal.IsKeyGenerated)
            {
                RunSafe(_keyManagerView.RefreshKeysAsync());
                RunSafe(_dashboardView.RefreshDashboardAsync());
            }
        }

        private void OpenImportModal()
        {
            var modal = new KeyImportWindow(_gpgService) { Owner = this };
            modal.ShowDialog();
            if (modal.IsKeyImported)
            {
                RunSafe(_keyManagerView.RefreshKeysAsync());
                RunSafe(_dashboardView.RefreshDashboardAsync());
            }
        }

        private void OpenSettingsModal()
        {
            var modal = new SettingsWindow(_gpgService) { Owner = this };
            modal.ShowDialog();
            if (modal.IsSettingsChanged)
            {
                RunSafe(_dashboardView.RefreshDashboardAsync());
            }
        }

        // Helper to run fire-and-forget tasks safely and report errors to UI thread
        private void RunSafe(Task t)
        {
            if (t == null) return;
            t.ContinueWith(task =>
            {
                if (task.IsFaulted)
                {
                    var ex = task.Exception != null ? task.Exception.GetBaseException() : null;
                    try
                    {
                        Dispatcher.Invoke(() =>
                        {
                            MessageBox.Show("内部エラー: " + (ex != null ? ex.Message : "不明なエラー"), "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                        });
                    }
                    catch { }
                }
            }, TaskScheduler.Default);
        }

        protected override void OnDragOver(DragEventArgs e)
        {
            base.OnDragOver(e);
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
            }
        }

        protected override void OnDrop(DragEventArgs e)
        {
            base.OnDrop(e);
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files != null && files.Length > 0)
                {
                    // 鍵保管庫画面を開いている場合のみ鍵インポートダイアログを開く
                    if (_contentArea.Content == _keyManagerView)
                    {
                        var modal = new KeyImportWindow(_gpgService, files) { Owner = this };
                        modal.ShowDialog();
                        if (modal.IsKeyImported)
                        {
                            RunSafe(_keyManagerView.RefreshKeysAsync());
                            RunSafe(_dashboardView.RefreshDashboardAsync());
                        }
                    }
                    else if (files.Any(f => System.IO.File.Exists(f) || System.IO.Directory.Exists(f)))
                    {
                        NavigateTo("Crypto");
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            RunSafe(_cryptoView.OpenFilesSmartAsync(files));
                        }), System.Windows.Threading.DispatcherPriority.Background);
                    }
                }
            }
        }
    }
}
