using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace GpgUi.Views
{
    public class KeyManagerView : UserControl
    {
        // ─── 依存サービス・コールバック ─────────────────────────────────────
        private readonly GpgService _gpgService;
        private readonly Action _openGeneratorAction;
        private readonly Action _openImportAction;

        // ─── 状態 ────────────────────────────────────────────────────────
        private List<GpgKey> _allKeys;
        private GpgKey _selectedKey;
        private string _sortColumn = "DisplayTitle";
        private bool _sortAscending = true;
        private bool _isRefreshing = false;
        private bool _isDeletingKeys = false;

        // ─── UI コントロール (ツールバー) ────────────────────────────────
        private TextBox _searchBox;
        private DispatcherTimer _searchDebounceTimer;
        private ComboBox _filterCombo;
        private Button _btnRefresh;

        // ─── UI コントロール (鍵一覧) ────────────────────────────────────
        private ListView _keyListView;
        private Border _loadingOverlay;
        private readonly List<KeyColumnInfo> _columns = new List<KeyColumnInfo>();

        // ─── UI コントロール (詳細カード) ────────────────────────────────
        private TextBlock _lblDetailTitle;
        private TextBlock _lblDetailFpr;
        private TextBlock _lblDetailKeyId;
        private TextBlock _lblDetailAlgo;
        private TextBlock _lblDetailDates;
        private StackPanel _badgePanel;
        private WrapPanel _detailBtnStack;
        private Button _btnExportPub;
        private Button _btnExportSec;
        private Button _btnDeleteKey;
        private Button _btnSignKey;

        // ─── 列情報 ──────────────────────────────────────────────────────
        private class KeyColumnInfo
        {
            public string ColumnKey { get; set; }
            public string Title { get; set; }
            public string BindingPath { get; set; }
            public GridViewColumn Column { get; set; }
        }

        // ─── 静的コンストラクタ ──────────────────────────────────────────
        static KeyManagerView()
        {
            // ListView 内の RequestBringIntoView をクラスレベルで抑止（スクロール位置のジャンプ防止）
            EventManager.RegisterClassHandler(typeof(ListViewItem), FrameworkElement.RequestBringIntoViewEvent,
                new RequestBringIntoViewEventHandler((s, e) => e.Handled = true), true);
            EventManager.RegisterClassHandler(typeof(ListView), FrameworkElement.RequestBringIntoViewEvent,
                new RequestBringIntoViewEventHandler((s, e) => e.Handled = true), true);
        }

        // ─── コンストラクタ ──────────────────────────────────────────────
        public KeyManagerView(GpgService gpgService, Action openGeneratorAction, Action openImportAction)
        {
            _allKeys = new List<GpgKey>();
            _gpgService = gpgService;
            _openGeneratorAction = openGeneratorAction;
            _openImportAction = openImportAction;

            // 鍵ファイルのドラッグ&ドロップでインポートダイアログを開く
            AllowDrop = true;
            PreviewDragOver += (s, e) =>
            {
                if (e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    e.Effects = DragDropEffects.Copy;
                    e.Handled = true;
                }
            };
            Drop += async (s, e) =>
            {
                e.Handled = true;
                if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
                var files = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (files == null || files.Length == 0) return;

                var dlg = new KeyImportWindow(_gpgService, files) { Owner = Window.GetWindow(this) };
                dlg.ShowDialog();
                if (dlg.IsKeyImported)
                    await RefreshKeysAsync();
            };

            InitializeUI();
            Loaded += async (s, e) => await RefreshKeysAsync();
        }

        // ─── UI 初期化 ───────────────────────────────────────────────────
        private void InitializeUI()
        {
            var mainGrid = new Grid { Margin = new Thickness(16) };
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            Grid.SetRow(BuildToolBar(), 0);
            mainGrid.Children.Add(BuildToolBar());

            var listContainer = BuildListContainer();
            Grid.SetRow(listContainer, 1);
            mainGrid.Children.Add(listContainer);

            var detailCard = CreateDetailCard();
            Grid.SetRow(detailCard, 2);
            mainGrid.Children.Add(detailCard);

            mainGrid.AddHandler(FrameworkElement.RequestBringIntoViewEvent,
                new RequestBringIntoViewEventHandler((s, e) => e.Handled = true), true);

            Content = mainGrid;
            UpdateDetailCardView();
        }

        // ─── ツールバー構築 ──────────────────────────────────────────────
        private Grid BuildToolBar()
        {
            var toolBar = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            toolBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            toolBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 140 });
            toolBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            toolBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // 検索ボックス（デバウンス付き）
            _searchDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _searchDebounceTimer.Tick += (s, e) => { _searchDebounceTimer.Stop(); ApplyFilter(); };

            _searchBox = ThemeHelper.CreateStyledTextBox();
            _searchBox.Height = 36;
            _searchBox.Margin = new Thickness(0, 0, 10, 0);
            _searchBox.TextChanged += (s, e) => { _searchDebounceTimer.Stop(); _searchDebounceTimer.Start(); };

            var lblSearch = new TextBlock
            {
                Text = "検索:",
                Foreground = ThemeHelper.BrushTextMuted,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };

            // フィルターコンボ
            _filterCombo = new ComboBox
            {
                Height = 36,
                Width = 130,
                Margin = new Thickness(0, 0, 10, 0),
                Background = ThemeHelper.BrushSurfaceDark,
                Foreground = ThemeHelper.BrushTextMain,
                BorderBrush = ThemeHelper.BrushBorder,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            _filterCombo.Items.Add("すべての鍵");
            _filterCombo.Items.Add("秘密鍵のみ");
            _filterCombo.SelectedIndex = 0;
            _filterCombo.SelectionChanged += async (s, e) => await RefreshKeysAsync();

            // アクションボタン
            var btnGen = ThemeHelper.CreatePrimaryButton("+ 鍵ペア生成", (s, e) => { if (_openGeneratorAction != null) _openGeneratorAction(); });
            btnGen.Margin = new Thickness(0, 0, 8, 0);

            var btnImport = ThemeHelper.CreateSecondaryButton("鍵インポート", (s, e) => { if (_openImportAction != null) _openImportAction(); });
            btnImport.Margin = new Thickness(0, 0, 8, 0);

            _btnRefresh = ThemeHelper.CreateSecondaryButton("再読み込み", async (s, e) => await RefreshKeysAsync());

            var actionsPanel = new StackPanel { Orientation = Orientation.Horizontal };
            actionsPanel.Children.Add(btnGen);
            actionsPanel.Children.Add(btnImport);
            actionsPanel.Children.Add(_btnRefresh);

            Grid.SetColumn(lblSearch, 0);
            Grid.SetColumn(_searchBox, 1);
            Grid.SetColumn(_filterCombo, 2);
            Grid.SetColumn(actionsPanel, 3);

            toolBar.Children.Add(lblSearch);
            toolBar.Children.Add(_searchBox);
            toolBar.Children.Add(_filterCombo);
            toolBar.Children.Add(actionsPanel);

            return toolBar;
        }

        // ─── 鍵一覧コンテナ構築 ──────────────────────────────────────────
        private Grid BuildListContainer()
        {
            _keyListView = new ListView
            {
                Background = ThemeHelper.BrushSurfaceDark,
                Foreground = ThemeHelper.BrushTextMain,
                BorderBrush = ThemeHelper.BrushBorder,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 0, 12),
                MinHeight = 200,
                SelectionMode = SelectionMode.Extended
            };
            ScrollViewer.SetHorizontalScrollBarVisibility(_keyListView, ScrollBarVisibility.Visible);
            ScrollViewer.SetVerticalScrollBarVisibility(_keyListView, ScrollBarVisibility.Visible);
            ScrollViewer.SetCanContentScroll(_keyListView, false);

            // 列定義
            var gridView = new GridView();
            AddColumn(gridView, "DisplayKeyType",       "種別",                     "DisplayKeyType",       72);
            AddColumn(gridView, "DisplayTitle",          "ユーザーID (名前 / メール)", "DisplayTitle",         240);
            AddColumn(gridView, "TrustDisplayText",      "信頼レベル",               "TrustDisplayText",     90);
            AddColumn(gridView, "KeyId",                 "キーID",                   "KeyId",                140);
            AddColumn(gridView, "DisplayAlgo",           "アルゴリズム",             "DisplayAlgo",          120);
            AddColumn(gridView, "DisplayCreationDate",   "作成日",                   "DisplayCreationDate",  120);
            AddColumn(gridView, "DisplayExpirationDate", "有効期限",                 "DisplayExpirationDate",120);
            AddColumn(gridView, "FormattedFingerprint",  "フィンガープリント",       "FormattedFingerprint", 240);
            UpdateHeaderTitles();
            _keyListView.View = gridView;

            _keyListView.ItemContainerStyle = BuildListViewItemStyle();

            _keyListView.SelectionChanged += (s, e) => UpdateDetailCardView();
            _keyListView.AddHandler(GridViewColumnHeader.ClickEvent, new RoutedEventHandler(OnColumnHeaderClick));
            _keyListView.AddHandler(FrameworkElement.RequestBringIntoViewEvent,
                new RequestBringIntoViewEventHandler((s, e) => e.Handled = true), true);

            _keyListView.PreviewKeyDown += async (s, e) =>
            {
                if (e.Key == Key.Delete)
                {
                    e.Handled = true;
                    await DeleteSelectedKeysAsync();
                }
            };

            // Shift+ホイールで横スクロール
            _keyListView.PreviewMouseWheel += (s, e) =>
            {
                if (Keyboard.Modifiers == ModifierKeys.Shift)
                {
                    var sv = GetVisualChild<ScrollViewer>(_keyListView);
                    if (sv != null)
                    {
                        sv.ScrollToHorizontalOffset(sv.HorizontalOffset - e.Delta);
                        e.Handled = true;
                    }
                }
            };

            // ローディングオーバーレイ
            _loadingOverlay = BuildLoadingOverlay();

            var container = new Grid();
            container.Children.Add(_keyListView);
            container.Children.Add(_loadingOverlay);

            return container;
        }

        // ─── ListViewItem スタイル構築 ────────────────────────────────────
        private static Style BuildListViewItemStyle()
        {
            var style = new Style(typeof(ListViewItem));
            style.Setters.Add(new Setter(Control.ForegroundProperty, new SolidColorBrush(Color.FromRgb(248, 250, 252))));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6, 8, 6, 8)));
            style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Left));
            style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
            style.Setters.Add(new EventSetter(FrameworkElement.RequestBringIntoViewEvent,
                new RequestBringIntoViewEventHandler((s, e) => e.Handled = true)));

            // 期限切れ行は透過度を下げて視覚的に区別
            var expiredTrigger = new DataTrigger { Binding = new Binding("IsExpired"), Value = true };
            expiredTrigger.Setters.Add(new Setter(Control.ForegroundProperty, new SolidColorBrush(Color.FromRgb(148, 163, 184))));
            expiredTrigger.Setters.Add(new Setter(UIElement.OpacityProperty, 0.5));
            style.Triggers.Add(expiredTrigger);

            // カスタムテンプレート（行ボーダー）
            var template = new ControlTemplate(typeof(ListViewItem));
            var border = new FrameworkElementFactory(typeof(Border));
            border.Name = "bd";
            border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            border.SetBinding(Border.PaddingProperty, new Binding("Padding") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
            border.SetValue(Border.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(51, 65, 85)));
            border.SetValue(Border.BorderThicknessProperty, new Thickness(0, 0, 0, 1));
            border.SetBinding(TextElement.ForegroundProperty, new Binding("Foreground") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
            border.AppendChild(new FrameworkElementFactory(typeof(GridViewRowPresenter)));
            template.VisualTree = border;

            // 選択トリガー
            var selTrigger = new Trigger { Property = ListViewItem.IsSelectedProperty, Value = true };
            selTrigger.Setters.Add(new Setter(Border.BackgroundProperty,      new SolidColorBrush(Color.FromRgb(12, 74, 110)))  { TargetName = "bd" });
            selTrigger.Setters.Add(new Setter(Border.BorderBrushProperty,     new SolidColorBrush(Color.FromRgb(6, 182, 212)))  { TargetName = "bd" });
            selTrigger.Setters.Add(new Setter(Border.BorderThicknessProperty, new Thickness(3, 0, 0, 1))                        { TargetName = "bd" });
            selTrigger.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
            template.Triggers.Add(selTrigger);

            // ホバートリガー
            var hoverTrigger = new Trigger { Property = ListViewItem.IsMouseOverProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(30, 58, 79)))      { TargetName = "bd" });
            hoverTrigger.Setters.Add(new Setter(Control.ForegroundProperty, new SolidColorBrush(Color.FromRgb(56, 189, 248))));
            template.Triggers.Add(hoverTrigger);

            // 選択中 + ホバー
            var selHover = new MultiTrigger();
            selHover.Conditions.Add(new Condition { Property = ListViewItem.IsSelectedProperty, Value = true });
            selHover.Conditions.Add(new Condition { Property = ListViewItem.IsMouseOverProperty, Value = true });
            selHover.Setters.Add(new Setter(Border.BackgroundProperty,      new SolidColorBrush(Color.FromRgb(3, 105, 161)))    { TargetName = "bd" });
            selHover.Setters.Add(new Setter(Border.BorderBrushProperty,     new SolidColorBrush(Color.FromRgb(6, 182, 212)))    { TargetName = "bd" });
            selHover.Setters.Add(new Setter(Border.BorderThicknessProperty, new Thickness(3, 0, 0, 1))                          { TargetName = "bd" });
            selHover.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
            template.Triggers.Add(selHover);

            style.Setters.Add(new Setter(Control.TemplateProperty, template));
            return style;
        }

        // ─── ローディングオーバーレイ構築 ────────────────────────────────
        private static Border BuildLoadingOverlay()
        {
            var loadingStack = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            loadingStack.Children.Add(new TextBlock
            {
                Text = "⏳ 鍵リストを読み込み中...",
                FontSize = 22,
                FontWeight = FontWeights.Bold,
                Foreground = ThemeHelper.BrushPrimaryCyan,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 10)
            });
            loadingStack.Children.Add(new TextBlock
            {
                Text = "GnuPG から鍵情報を取得しています。しばらくお待ちください...",
                FontSize = 14,
                Foreground = ThemeHelper.BrushTextMuted,
                HorizontalAlignment = HorizontalAlignment.Center
            });

            return new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(220, 15, 23, 42)),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Visibility = Visibility.Collapsed,
                CornerRadius = new CornerRadius(6),
                Child = loadingStack
            };
        }

        // ─── 詳細カード構築 ──────────────────────────────────────────────
        private Border CreateDetailCard()
        {
            var pnl = new StackPanel();

            // タイトル行（鍵名 + バッジ）
            var headerStack = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
            _lblDetailTitle = new TextBlock { FontSize = 18, FontWeight = FontWeights.Bold, Foreground = ThemeHelper.BrushTextMain, VerticalAlignment = VerticalAlignment.Center };
            _badgePanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            headerStack.Children.Add(_lblDetailTitle);
            headerStack.Children.Add(_badgePanel);
            pnl.Children.Add(headerStack);

            // 鍵情報グリッド
            _lblDetailFpr   = new TextBlock { FontSize = 13, Foreground = ThemeHelper.BrushPrimaryCyan, FontFamily = new FontFamily("Consolas") };
            _lblDetailKeyId = new TextBlock { FontSize = 13, Foreground = ThemeHelper.BrushTextMain,    FontFamily = new FontFamily("Consolas") };
            _lblDetailAlgo  = new TextBlock { FontSize = 13, Foreground = ThemeHelper.BrushTextMain };
            _lblDetailDates = new TextBlock { FontSize = 13, Foreground = ThemeHelper.BrushTextMuted };

            var infoGrid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            infoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            infoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            AddInfoRow(infoGrid, "フィンガープリント:", _lblDetailFpr,   0);
            AddInfoRow(infoGrid, "キーID:",             _lblDetailKeyId, 1);
            AddInfoRow(infoGrid, "アルゴリズム:",       _lblDetailAlgo,  2);
            AddInfoRow(infoGrid, "作成・有効期限:",     _lblDetailDates, 3);
            pnl.Children.Add(infoGrid);

            // アクションボタン（折り返し対応）
            _detailBtnStack = new WrapPanel { Orientation = Orientation.Horizontal };

            var btnCopyFpr = ThemeHelper.CreateSecondaryButton("フィンガープリントをコピー",
                (s, e) => ThemeHelper.CopyTextToClipboard(_selectedKey != null ? _selectedKey.Fingerprint : null));
            btnCopyFpr.Margin = new Thickness(0, 0, 8, 4);

            var btnCopyKeyId = ThemeHelper.CreateSecondaryButton("キーIDをコピー",
                (s, e) => ThemeHelper.CopyTextToClipboard(_selectedKey != null ? _selectedKey.KeyId : null));
            btnCopyKeyId.Margin = new Thickness(0, 0, 8, 4);

            _btnExportPub = ThemeHelper.CreatePrimaryButton("公開鍵を出力",   async (s, e) => await ExportKeyAsync(false));
            _btnExportPub.Margin = new Thickness(0, 0, 8, 4);
            _btnExportSec = ThemeHelper.CreateSecondaryButton("秘密鍵を出力", async (s, e) => await ExportKeyAsync(true));
            _btnExportSec.Margin = new Thickness(0, 0, 8, 4);
            _btnSignKey   = ThemeHelper.CreateSecondaryButton("鍵に署名 (認証)", async (s, e) => await ShowSignKeyDialogAsync());
            _btnSignKey.Margin = new Thickness(0, 0, 8, 4);
            _btnDeleteKey = ThemeHelper.CreateDangerButton("鍵を削除",        async (s, e) => await DeleteSelectedKeysAsync());
            _btnDeleteKey.Margin = new Thickness(0, 0, 8, 4);

            _detailBtnStack.Children.Add(btnCopyFpr);
            _detailBtnStack.Children.Add(btnCopyKeyId);
            _detailBtnStack.Children.Add(_btnExportPub);
            _detailBtnStack.Children.Add(_btnExportSec);
            _detailBtnStack.Children.Add(_btnSignKey);
            _detailBtnStack.Children.Add(_btnDeleteKey);
            pnl.Children.Add(_detailBtnStack);

            return ThemeHelper.CreateCardPanel(pnl, 14);
        }

        /// <summary>詳細カード内の情報グリッドに1行追加する</summary>
        private static void AddInfoRow(Grid grid, string label, UIElement valueElement, int row)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var lbl = new TextBlock { Text = label, Foreground = ThemeHelper.BrushTextMuted, FontSize = 13, Margin = new Thickness(0, 3, 0, 3) };
            Grid.SetRow(lbl, row); Grid.SetColumn(lbl, 0);
            grid.Children.Add(lbl);

            Grid.SetRow(valueElement, row); Grid.SetColumn(valueElement, 1);
            grid.Children.Add(valueElement);
        }

        // ─── 詳細カード更新 ──────────────────────────────────────────────
        private void UpdateDetailCardView()
        {
            var selected = _keyListView.SelectedItems.Cast<GpgKey>().ToList();

            if (selected.Count == 0)
            {
                _selectedKey = null;
                _lblDetailTitle.Text = "鍵を選択してください";
                _lblDetailFpr.Text   = "一覧から鍵を選択すると、ここに詳細情報が表示されます。";
                _lblDetailKeyId.Text = "-";
                _lblDetailAlgo.Text  = "-";
                _lblDetailDates.Text = "-";
                _badgePanel.Children.Clear();
                _detailBtnStack.Visibility = Visibility.Hidden;
                return;
            }

            _detailBtnStack.Visibility = Visibility.Visible;

            if (selected.Count > 1)
            {
                _selectedKey = selected[0];
                _lblDetailTitle.Text = string.Format("複数選択中 ({0} 件の鍵)", selected.Count);
                _lblDetailFpr.Text   = "Ctrl または Shift キーを押しながらクリックして複数選択可能です。Del キーでまとめて削除できます。";
                _lblDetailKeyId.Text = string.Format("{0} 件選択済み", selected.Count);
                _lblDetailAlgo.Text  = string.Join(", ", selected.Select(k => k.DisplayAlgo).Distinct());
                _lblDetailDates.Text = string.Format("選択中の全 {0} 件が対象", selected.Count);

                _badgePanel.Children.Clear();
                int secCount = selected.Count(k => k.IsSecretKey);
                int pubCount = selected.Count - secCount;
                if (secCount > 0) _badgePanel.Children.Add(ThemeHelper.CreateBadge(string.Format("秘密鍵: {0} 件", secCount), ThemeHelper.AccentViolet, Colors.White));
                if (pubCount > 0) _badgePanel.Children.Add(ThemeHelper.CreateBadge(string.Format("公開鍵: {0} 件", pubCount), ThemeHelper.SurfaceHover, ThemeHelper.TextMain));

                _btnExportPub.Visibility = Visibility.Collapsed;
                _btnExportSec.Visibility = Visibility.Collapsed;
                _btnSignKey.Visibility   = Visibility.Collapsed;
                _btnDeleteKey.Content    = string.Format("選択した {0} 件の鍵を削除 (Del)", selected.Count);
                _btnDeleteKey.Visibility = Visibility.Visible;
            }
            else
            {
                _selectedKey = selected[0];
                _lblDetailTitle.Text = _selectedKey.DisplayTitle;
                _lblDetailFpr.Text   = _selectedKey.FormattedFingerprint;
                _lblDetailKeyId.Text = _selectedKey.KeyId;
                _lblDetailAlgo.Text  = string.Format("{0} ({1})", _selectedKey.DisplayAlgo, _selectedKey.Algorithm);

                string created = _selectedKey.CreationDate.HasValue ? _selectedKey.CreationDate.Value.ToString("yyyy-MM-dd") : "不明";
                string expires = _selectedKey.ExpirationDate.HasValue
                    ? string.Format(" | 有効期限: {0:yyyy-MM-dd}", _selectedKey.ExpirationDate.Value)
                    : " | 有効期限: 無期限";
                _lblDetailDates.Text = "作成日: " + created + expires;

                _badgePanel.Children.Clear();
                _badgePanel.Children.Add(_selectedKey.IsSecretKey
                    ? ThemeHelper.CreateBadge("秘密鍵 (SECRET)", ThemeHelper.AccentViolet, Colors.White)
                    : ThemeHelper.CreateBadge("公開鍵 (PUBLIC)", ThemeHelper.SurfaceHover, ThemeHelper.TextMain));

                _btnExportPub.Visibility = Visibility.Visible;
                _btnExportSec.Visibility = _selectedKey.IsSecretKey ? Visibility.Visible   : Visibility.Collapsed;
                _btnSignKey.Visibility   = _selectedKey.IsSecretKey ? Visibility.Collapsed : Visibility.Visible;
                _btnDeleteKey.Content    = "鍵を削除 (Del)";
                _btnDeleteKey.Visibility = Visibility.Visible;
            }
        }

        // ─── 鍵一覧リフレッシュ ──────────────────────────────────────────
        public async Task RefreshKeysAsync()
        {
            if (_isRefreshing) return;
            _isRefreshing = true;

            try
            {
                _loadingOverlay.Visibility = Visibility.Visible;
                _btnRefresh.IsEnabled      = false;
                _filterCombo.IsEnabled     = false;

                _allKeys = await _gpgService.GetKeysAsync();
                ApplyFilter();
            }
            finally
            {
                _loadingOverlay.Visibility = Visibility.Collapsed;
                _btnRefresh.IsEnabled      = true;
                _filterCombo.IsEnabled     = true;
                _isRefreshing = false;
                UpdateDetailCardView();
            }
        }

        // ─── フィルター・ソート ──────────────────────────────────────────
        private void ApplyFilter()
        {
            if (_allKeys == null) return;

            string query    = (_searchBox.Text ?? "").ToLower().Trim();
            bool secretOnly = _filterCombo.SelectedIndex == 1;

            var filtered = _allKeys.Where(k =>
            {
                if (secretOnly && !k.IsSecretKey) return false;
                if (string.IsNullOrEmpty(query))  return true;
                return (k.DisplayTitle ?? "").ToLower().Contains(query)
                    || (k.Fingerprint  ?? "").ToLower().Contains(query)
                    || (k.KeyId        ?? "").ToLower().Contains(query);
            });

            _keyListView.ItemsSource = SortKeys(filtered).ToList();
        }

        private IEnumerable<GpgKey> SortKeys(IEnumerable<GpgKey> keys)
        {
            switch (_sortColumn)
            {
                case "DisplayKeyType":       return Order(keys, k => k.DisplayKeyType);
                case "DisplayTitle":         return Order(keys, k => k.DisplayTitle ?? "");
                case "TrustDisplayText":     return Order(keys, k => k.TrustDisplayText ?? "");
                case "KeyId":                return Order(keys, k => k.KeyId ?? "");
                case "DisplayAlgo":          return Order(keys, k => k.DisplayAlgo ?? "");
                case "DisplayCreationDate":  return Order(keys, k => k.CreationDate ?? DateTime.MinValue);
                case "DisplayExpirationDate":return Order(keys, k => k.ExpirationDate ?? DateTime.MaxValue);
                case "FormattedFingerprint": return Order(keys, k => k.Fingerprint ?? "");
                default:                     return Order(keys, k => k.DisplayTitle ?? "");
            }
        }

        private IOrderedEnumerable<GpgKey> Order<TKey>(IEnumerable<GpgKey> keys, Func<GpgKey, TKey> selector)
        {
            return _sortAscending ? keys.OrderBy(selector) : keys.OrderByDescending(selector);
        }

        // ─── 列追加・ヘッダー更新・ソート ────────────────────────────────
        private void AddColumn(GridView gridView, string key, string title, string bindingPath, double width)
        {
            var col = new GridViewColumn { Header = title, DisplayMemberBinding = new Binding(bindingPath), Width = width };
            _columns.Add(new KeyColumnInfo { ColumnKey = key, Title = title, BindingPath = bindingPath, Column = col });
            gridView.Columns.Add(col);
        }

        private void OnColumnHeaderClick(object sender, RoutedEventArgs e)
        {
            var header = e.OriginalSource as GridViewColumnHeader;
            if (header == null || header.Column == null) return;

            var match = _columns.FirstOrDefault(c => c.Column == header.Column);
            if (match == null || match.ColumnKey == null) return;

            _sortAscending = (_sortColumn == match.ColumnKey) ? !_sortAscending : true;
            _sortColumn    = match.ColumnKey;
            UpdateHeaderTitles();
            ApplyFilter();
        }

        private void UpdateHeaderTitles()
        {
            foreach (var col in _columns)
                col.Column.Header = (col.ColumnKey == _sortColumn)
                    ? col.Title + (_sortAscending ? " ▲" : " ▼")
                    : col.Title;
        }

        // ─── 鍵エクスポート ──────────────────────────────────────────────
        private async Task ExportKeyAsync(bool isSecret)
        {
            if (_selectedKey == null) return;

            string armorData = isSecret
                ? await _gpgService.ExportSecretKeyAsync(_selectedKey.Fingerprint, "")
                : await _gpgService.ExportPublicKeyAsync(_selectedKey.Fingerprint);

            if (string.IsNullOrEmpty(armorData))
            {
                ThemeHelper.ShowInfoModal(Window.GetWindow(this), "鍵のエクスポートに失敗しました。", "エラー");
                return;
            }

            var dlg = new Window
            {
                Title = isSecret ? "秘密鍵のエクスポート (ASCII Armor)" : "公開鍵のエクスポート (ASCII Armor)",
                Width = 600,
                Height = 450,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Window.GetWindow(this),
                Background = ThemeHelper.BrushBgDark
            };
            ThemeHelper.ApplyDarkTitleBar(dlg);

            var txt = ThemeHelper.CreateStyledTextBox(armorData, true);
            txt.AcceptsReturn = true;
            txt.TextWrapping = TextWrapping.Wrap;
            txt.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            txt.FontFamily = new FontFamily("Consolas");

            var btnCopy = ThemeHelper.CreatePrimaryButton("ASCII Armor をコピー", (s, e) =>
            {
                Clipboard.SetText(armorData);
                ThemeHelper.ShowInfoModal(dlg, "ASCII Armor をクリップボードにコピーしました！", "成功");
            });

            var btnSave = ThemeHelper.CreateSecondaryButton("ファイルに保存", (s, e) =>
            {
                var sfd = new Microsoft.Win32.SaveFileDialog
                {
                    Filter   = "GPG 鍵ファイル (*.asc;*.pub;*.sec)|*.asc;*.pub;*.sec|すべてのファイル (*.*)|*.*",
                    FileName = string.Format("{0}_{1}.asc", _selectedKey.KeyId, isSecret ? "secret" : "public")
                };
                if (sfd.ShowDialog() == true)
                {
                    System.IO.File.WriteAllText(sfd.FileName, armorData);
                    ThemeHelper.ShowInfoModal(dlg, "鍵ファイルを保存しました！", "成功");
                }
            });

            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal };
            btnPanel.Children.Add(btnCopy);
            btnPanel.Children.Add(btnSave);

            var pnl = new StackPanel { Margin = new Thickness(16) };
            pnl.Children.Add(new Border { Child = txt, Height = 320, Margin = new Thickness(0, 0, 0, 12) });
            pnl.Children.Add(btnPanel);

            dlg.Content = pnl;
            dlg.ShowDialog();
        }

        // ─── 鍵署名ダイアログ ────────────────────────────────────────────
        private async Task ShowSignKeyDialogAsync()
        {
            if (_selectedKey == null) return;

            var secretKeys = _allKeys.Where(k => k.IsSecretKey).ToList();
            if (secretKeys.Count == 0)
            {
                ThemeHelper.ShowInfoModal(Window.GetWindow(this), "署名に使用できる秘密鍵がキーリングにありません。", "警告");
                return;
            }

            var dlg = new Window
            {
                Title  = string.Format("鍵への署名 (認証): {0}", _selectedKey.DisplayTitle),
                Width  = 540,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner  = Window.GetWindow(this),
                Background = ThemeHelper.BrushBgDark,
                ResizeMode = ResizeMode.NoResize
            };
            ThemeHelper.ApplyDarkTitleBar(dlg);

            var pnl = new StackPanel { Margin = new Thickness(24) };

            pnl.Children.Add(new TextBlock
            {
                Text = string.Format("対象: {0}", _selectedKey.DisplayTitle),
                Foreground = ThemeHelper.BrushTextMain,
                FontSize = 14, FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 4), TextWrapping = TextWrapping.Wrap
            });
            pnl.Children.Add(new TextBlock
            {
                Text = string.Format("フィンガープリント: {0}", _selectedKey.FormattedFingerprint),
                Foreground = ThemeHelper.BrushPrimaryCyan,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12, Margin = new Thickness(0, 0, 0, 16), TextWrapping = TextWrapping.Wrap
            });

            pnl.Children.Add(new TextBlock { Text = "署名に使用する秘密鍵:", Foreground = ThemeHelper.BrushTextMuted, FontSize = 13, Margin = new Thickness(0, 0, 0, 4) });
            var cboSigner = new ComboBox
            {
                Height = 36, Background = ThemeHelper.BrushBgDark,
                Foreground = ThemeHelper.BrushTextMain, BorderBrush = ThemeHelper.BrushBorder,
                VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 12)
            };
            foreach (var sk in secretKeys) cboSigner.Items.Add(sk.DisplayTitle);
            cboSigner.SelectedIndex = 0;
            pnl.Children.Add(cboSigner);

            pnl.Children.Add(new TextBlock { Text = "パスフレーズ:", Foreground = ThemeHelper.BrushTextMuted, FontSize = 13, Margin = new Thickness(0, 0, 0, 4) });
            var txtPass = ThemeHelper.CreateStyledPasswordBox();
            txtPass.Margin = new Thickness(0, 0, 0, 12);
            pnl.Children.Add(txtPass);

            pnl.Children.Add(new TextBlock { Text = "署名の種類:", Foreground = ThemeHelper.BrushTextMuted, FontSize = 13, Margin = new Thickness(0, 0, 0, 4) });
            var cboMode = new ComboBox
            {
                Height = 36, Background = ThemeHelper.BrushBgDark,
                Foreground = ThemeHelper.BrushTextMain, BorderBrush = ThemeHelper.BrushBorder,
                VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 20)
            };
            cboMode.Items.Add("完全署名 (--sign-key, 他者へエクスポート可能)");
            cboMode.Items.Add("ローカル署名 (--lsign-key, 自分のみ有効)");
            cboMode.SelectedIndex = 1; // 安全側のローカル署名をデフォルト
            pnl.Children.Add(cboMode);

            var btnOk = ThemeHelper.CreatePrimaryButton("署名を実行", null);
            btnOk.MinWidth = 110;
            btnOk.Margin = new Thickness(0, 0, 8, 0);
            var btnCancel = ThemeHelper.CreateSecondaryButton("キャンセル", (s, e) => dlg.DialogResult = false);
            btnCancel.MinWidth = 90;
            btnCancel.IsCancel = true;

            var btnStack = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            btnStack.Children.Add(btnOk);
            btnStack.Children.Add(btnCancel);
            pnl.Children.Add(btnStack);

            dlg.Content = pnl;

            bool executed = false;
            btnOk.Click += async (s, e) =>
            {
                int signerIdx = cboSigner.SelectedIndex;
                if (signerIdx < 0 || signerIdx >= secretKeys.Count)
                {
                    ThemeHelper.ShowInfoModal(dlg, "署名に使用する秘密鍵を選択してください。", "警告");
                    return;
                }

                btnOk.IsEnabled = false;
                btnOk.Content   = "処理中...";

                var result = await _gpgService.SignKeyAsync(
                    _selectedKey.Fingerprint, secretKeys[signerIdx].Fingerprint,
                    txtPass.Password, cboMode.SelectedIndex == 1);

                if (result.Success)
                {
                    executed = true;
                    dlg.DialogResult = true;
                }
                else
                {
                    ThemeHelper.ShowInfoModal(dlg, "署名に失敗しました:\n" + result.ErrorMessage, "エラー");
                    btnOk.IsEnabled = true;
                    btnOk.Content   = "署名を実行";
                }
            };

            dlg.ShowDialog();

            if (executed)
            {
                ThemeHelper.ShowInfoModal(Window.GetWindow(this), "鍵への署名が完了しました。\n信頼レベルが更新されます。", "署名成功");
                await RefreshKeysAsync();
            }
        }

        // ─── 鍵削除 ──────────────────────────────────────────────────────
        private async Task DeleteSelectedKeysAsync()
        {
            if (_isDeletingKeys) return;
            _isDeletingKeys = true;

            try
            {
                var targets = _keyListView.SelectedItems.Cast<GpgKey>().ToList();
                if (targets.Count == 0)
                {
                    if (_selectedKey == null) return;
                    targets.Add(_selectedKey);
                }

                string confirmMsg = (targets.Count == 1)
                    ? string.Format("本当に鍵を削除しますか？:\n{0} ({1})\n\nこの操作は取り消せません！", targets[0].DisplayTitle, targets[0].KeyId)
                    : string.Format("選択された {0} 件の鍵をまとめて削除しますか？\n\nこの操作は取り消せません！", targets.Count);

                var parentWin = Window.GetWindow(this);
                if (!ThemeHelper.ShowConfirmModal(parentWin, confirmMsg, "削除の確認")) return;

                int successCount = 0, failCount = 0;
                var errors = new List<string>();

                foreach (var key in targets)
                {
                    var result = await _gpgService.DeleteKeyAsync(key.Fingerprint, key.IsSecretKey);
                    if (result.Success) successCount++;
                    else { failCount++; errors.Add(string.Format("{0}: {1}", key.DisplayTitle, result.ErrorMessage)); }
                }

                string resultMsg = (failCount == 0)
                    ? string.Format("{0} 件の鍵を正常に削除しました。", successCount)
                    : string.Format("削除処理完了:\n成功: {0} 件\n失敗: {1} 件\n\nエラー詳細:\n{2}", successCount, failCount, string.Join("\n", errors));
                ThemeHelper.ShowInfoModal(parentWin, resultMsg, failCount == 0 ? "削除完了" : "削除結果");

                await RefreshKeysAsync();
            }
            finally
            {
                _isDeletingKeys = false;
            }
        }

        // ─── ユーティリティ ──────────────────────────────────────────────
        /// <summary>ビジュアルツリーを再帰的に検索して型 T の最初の子孫を返す</summary>
        private static T GetVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) return null;
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T) return (T)child;
                var found = GetVisualChild<T>(child);
                if (found != null) return found;
            }
            return null;
        }
    }
}
