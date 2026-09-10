using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GpgUi.Views
{
    public class DashboardView : UserControl
    {
        private readonly GpgService _gpgService;

        private TextBlock _txtVersion;
        private TextBlock _txtHomeDir;
        private TextBlock _txtExePath;
        private TextBlock _txtPubKeyCount;
        private TextBlock _txtSecKeyCount;

        public DashboardView(GpgService gpgService)
        {
            _gpgService = gpgService;

            InitializeUI();
            Loaded += async (s, e) => await RefreshDashboardAsync();
        }

        private void InitializeUI()
        {
            var mainGrid = new Grid { Margin = new Thickness(16) };
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var header = new TextBlock
            {
                Text = "GnuPG システムダッシュボード",
                FontSize = 22,
                FontWeight = FontWeights.Bold,
                Foreground = ThemeHelper.BrushTextMain,
                Margin = new Thickness(0, 0, 0, 16)
            };
            Grid.SetRow(header, 0);
            mainGrid.Children.Add(header);

            var cardsGrid = new Grid { Margin = new Thickness(0, 0, 0, 20) };
            cardsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            cardsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            _txtPubKeyCount = new TextBlock { Text = "-", FontSize = 32, FontWeight = FontWeights.Bold, Foreground = ThemeHelper.BrushPrimaryCyan };
            _txtSecKeyCount = new TextBlock { Text = "-", FontSize = 32, FontWeight = FontWeights.Bold, Foreground = ThemeHelper.BrushAccentViolet };

            var c1 = CreateStatCard("公開鍵数", _txtPubKeyCount, "登録済みのパブリックキー", 0);
            var c2 = CreateStatCard("秘密鍵数", _txtSecKeyCount, "所有している秘密鍵ペア", 1);

            cardsGrid.Children.Add(c1);
            cardsGrid.Children.Add(c2);

            Grid.SetRow(cardsGrid, 1);
            mainGrid.Children.Add(cardsGrid);

            var sysInfoPanel = new StackPanel();
            sysInfoPanel.Children.Add(new TextBlock { Text = "GnuPG 環境ステータス", FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = ThemeHelper.BrushTextMain, Margin = new Thickness(0, 0, 0, 12) });

            _txtVersion = new TextBlock { Text = "読み込み中...", FontSize = 13, Foreground = ThemeHelper.BrushTextMain, Margin = new Thickness(0, 0, 0, 6) };
            _txtExePath = new TextBlock { Text = "読み込み中...", FontSize = 13, Foreground = ThemeHelper.BrushTextMuted, Margin = new Thickness(0, 0, 0, 6) };
            _txtHomeDir = new TextBlock { Text = "読み込み中...", FontSize = 13, Foreground = ThemeHelper.BrushTextMuted };

            sysInfoPanel.Children.Add(_txtVersion);
            sysInfoPanel.Children.Add(_txtExePath);
            sysInfoPanel.Children.Add(_txtHomeDir);

            var sysCard = ThemeHelper.CreateCardPanel(sysInfoPanel, 20);
            Grid.SetRow(sysCard, 2);
            mainGrid.Children.Add(sysCard);

            Content = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = mainGrid
            };
        }

        private Border CreateStatCard(string title, UIElement content, string subtext, int col)
        {
            var pnl = new StackPanel();
            pnl.Children.Add(new TextBlock { Text = title, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = ThemeHelper.BrushTextMuted, Margin = new Thickness(0, 0, 0, 8) });
            pnl.Children.Add(content);
            pnl.Children.Add(new TextBlock { Text = subtext, FontSize = 12, Foreground = ThemeHelper.BrushTextMuted, Margin = new Thickness(0, 6, 0, 0) });

            var card = ThemeHelper.CreateCardPanel(pnl, 20);
            Grid.SetColumn(card, col);
            return card;
        }

        private bool _isRefreshing = false;

        public async Task RefreshDashboardAsync()
        {
            if (_isRefreshing) return;
            _isRefreshing = true;

            try
            {
                Dispatcher.Invoke((Action)(() =>
                {
                    _txtPubKeyCount.Text = "⏳";
                    _txtSecKeyCount.Text = "⏳";
                    _txtVersion.Text = "⏳ GnuPG 情報を読み込み中...";
                    _txtExePath.Text = "⏳ 読み込み中...";
                    _txtHomeDir.Text = "⏳ 読み込み中...";
                }));

                var sysInfoTask = _gpgService.GetSystemInfoAsync();
                var keysTask = _gpgService.GetKeysAsync();

                await Task.WhenAll(sysInfoTask, keysTask).ConfigureAwait(false);

                var sysInfo = sysInfoTask.Result;
                var keys = keysTask.Result;

                int pubCount = keys != null ? keys.Count : 0;
                int secCount = 0;
                if (keys != null)
                {
                    foreach (var k in keys)
                    {
                        if (k.IsSecretKey) secCount++;
                    }
                }

                Dispatcher.Invoke((Action)(() =>
                {
                    if (sysInfo != null && !sysInfo.IsAvailable)
                    {
                        _txtVersion.Text = "バージョン: ⚠️ " + (string.IsNullOrEmpty(sysInfo.Version) ? "gpg.exe が見つかりません" : sysInfo.Version);
                        _txtVersion.Foreground = ThemeHelper.BrushDangerRed;
                        _txtExePath.Text = "gpg.exe パス: " + _gpgService.GpgPath;
                        _txtHomeDir.Text = "GnuPG ホーム: 未検出（「設定」から gpg.exe を選択してください）";
                    }
                    else
                    {
                        _txtVersion.Text = "バージョン: " + (sysInfo != null ? sysInfo.Version : "");
                        _txtVersion.Foreground = ThemeHelper.BrushTextMain;
                        _txtExePath.Text = "gpg.exe パス: " + _gpgService.GpgPath;
                        _txtHomeDir.Text = "GnuPG ホーム: " + (sysInfo != null && !string.IsNullOrEmpty(sysInfo.HomeDirectory) ? sysInfo.HomeDirectory : "未検出");
                    }
                    _txtPubKeyCount.Text = pubCount.ToString();
                    _txtSecKeyCount.Text = secCount.ToString();
                }));
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke((Action)(() =>
                {
                    _txtVersion.Text = "エラー: GnuPG 情報の読み込みに失敗しました (" + ex.Message + ")";
                    _txtVersion.Foreground = ThemeHelper.BrushDangerRed;
                    _txtExePath.Text = "gpg.exe パス: " + _gpgService.GpgPath;
                    _txtHomeDir.Text = "GnuPG ホーム: エラー";
                    _txtPubKeyCount.Text = "0";
                    _txtSecKeyCount.Text = "0";
                }));
            }
            finally
            {
                _isRefreshing = false;
            }
        }
    }
}
