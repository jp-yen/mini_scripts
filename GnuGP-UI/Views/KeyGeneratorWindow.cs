using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GpgUi.Views
{
    public class KeyGeneratorWindow : Window
    {
        private readonly GpgService _gpgService;

        private TextBox _txtName;
        private TextBox _txtEmail;
        private TextBox _txtComment;
        private ComboBox _cboAlgo;
        private ComboBox _cboExpire;
        private PasswordBox _txtPassphrase;
        private PasswordBox _txtConfirmPassphrase;
        private TextBlock _lblStatus;
        private Button _btnGenerate;

        public bool IsKeyGenerated { get; private set; }

        public KeyGeneratorWindow(GpgService gpgService)
        {
            _gpgService = gpgService;
            IsKeyGenerated = false;

            Title = "新規 GnuPG 鍵ペアの生成";
            Width = 560;
            Height = 520;
            MinWidth = 480;
            MinHeight = 400;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = ThemeHelper.BrushBgDark;
            ResizeMode = ResizeMode.CanResizeWithGrip;

            InitializeUI();
            ThemeHelper.ApplyDarkTitleBar(this);
        }

        private void InitializeUI()
        {
            var mainGrid = new Grid { Margin = new Thickness(20) };
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var header = new TextBlock
            {
                Text = "新規鍵ペアの作成",
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                Foreground = ThemeHelper.BrushTextMain,
                Margin = new Thickness(0, 0, 0, 16)
            };
            Grid.SetRow(header, 0);
            mainGrid.Children.Add(header);

            var formGrid = new Grid();
            formGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            formGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            _txtName = ThemeHelper.CreateStyledTextBox();
            _txtEmail = ThemeHelper.CreateStyledTextBox();
            _txtComment = ThemeHelper.CreateStyledTextBox();

            _cboAlgo = new ComboBox
            {
                Height = 36,
                Background = ThemeHelper.BrushSurfaceDark,
                Foreground = ThemeHelper.BrushTextMain,
                BorderBrush = ThemeHelper.BrushBorder,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            _cboAlgo.Items.Add("Ed25519 (Ed25519 署名 + Cv25519 暗号化)");
            _cboAlgo.Items.Add("Ed448 (Ed448 署名 + Cv448 暗号化)");
            _cboAlgo.SelectedIndex = 0;

            _cboExpire = new ComboBox
            {
                Height = 36,
                Background = ThemeHelper.BrushSurfaceDark,
                Foreground = ThemeHelper.BrushTextMain,
                BorderBrush = ThemeHelper.BrushBorder,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            _cboExpire.Items.Add("無期限");
            _cboExpire.Items.Add("1 年");
            _cboExpire.Items.Add("2 年");
            _cboExpire.Items.Add("5 年");
            _cboExpire.SelectedIndex = 2;

            _txtPassphrase = ThemeHelper.CreateStyledPasswordBox();
            _txtConfirmPassphrase = ThemeHelper.CreateStyledPasswordBox();

            int r = 0;
            AddFormRow(formGrid, "氏名 (Name) *:", _txtName, r++);
            AddFormRow(formGrid, "メールアドレス (Email):", _txtEmail, r++);
            AddFormRow(formGrid, "コメント (Comment):", _txtComment, r++);
            AddFormRow(formGrid, "アルゴリズム *:", _cboAlgo, r++);
            AddFormRow(formGrid, "有効期限:", _cboExpire, r++);
            AddFormRow(formGrid, "パスフレーズ:", _txtPassphrase, r++);
            AddFormRow(formGrid, "パスフレーズ確認:", _txtConfirmPassphrase, r++);

            Grid.SetRow(formGrid, 1);
            mainGrid.Children.Add(formGrid);

            var actionStack = new StackPanel();

            _lblStatus = new TextBlock
            {
                Text = "",
                Foreground = ThemeHelper.BrushDangerRed,
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 10),
                TextWrapping = TextWrapping.Wrap
            };
            actionStack.Children.Add(_lblStatus);

            var btnPnl = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            _btnGenerate = ThemeHelper.CreatePrimaryButton("鍵ペアを生成", async (s, e) => await OnGenerateClickAsync());
            var btnCancel = ThemeHelper.CreateSecondaryButton("キャンセル", (s, e) => Close());
            btnCancel.IsCancel = true;

            btnPnl.Children.Add(btnCancel);
            btnPnl.Children.Add(_btnGenerate);
            actionStack.Children.Add(btnPnl);

            Grid.SetRow(actionStack, 2);
            mainGrid.Children.Add(actionStack);

            Content = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = mainGrid
            };
        }

        private void AddFormRow(Grid g, string label, UIElement element, int rowIdx)
        {
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var lbl = new TextBlock
            {
                Text = label,
                Foreground = ThemeHelper.BrushTextMuted,
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 4, 8, 4)
            };

            Grid.SetRow(lbl, rowIdx);
            Grid.SetColumn(lbl, 0);
            g.Children.Add(lbl);

            Grid.SetRow(element, rowIdx);
            Grid.SetColumn(element, 1);
            g.Children.Add(element);
        }

        private async Task OnGenerateClickAsync()
        {
            string name = _txtName.Text.Trim();
            string email = _txtEmail.Text.Trim();
            string comment = _txtComment.Text.Trim();
            string pass = _txtPassphrase.Password;
            string confirmPass = _txtConfirmPassphrase.Password;

            if (string.IsNullOrEmpty(name))
            {
                _lblStatus.Text = "エラー: 氏名は必須項目です。";
                return;
            }

            if (pass != confirmPass)
            {
                _lblStatus.Text = "エラー: パスフレーズが一致しません。";
                return;
            }

            string algoChoice = _cboAlgo.SelectedIndex == 1 ? "Ed448" : "Ed25519";

            string expireOption = "Never";
            if (_cboExpire.SelectedIndex == 1) expireOption = "1 Year";
            else if (_cboExpire.SelectedIndex == 2) expireOption = "2 Years";
            else if (_cboExpire.SelectedIndex == 3) expireOption = "5 Years";

            _lblStatus.Foreground = ThemeHelper.BrushPrimaryCyan;
            _lblStatus.Text = "鍵ペアを生成中... 数秒かかります...";
            _btnGenerate.IsEnabled = false;

            try
            {
                var result = await _gpgService.GenerateKeyAsync(name, email, comment, algoChoice, expireOption, pass);
                if (result.Success)
                {
                    IsKeyGenerated = true;
                    MessageBox.Show(string.Format("新しく {0} 鍵ペアが正常に生成されました！", algoChoice), "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                    Close();
                }
                else
                {
                    _lblStatus.Foreground = ThemeHelper.BrushDangerRed;
                    _lblStatus.Text = "生成失敗: " + result.ErrorMessage;
                    _btnGenerate.IsEnabled = true;
                }
            }
            catch (Exception ex)
            {
                _lblStatus.Foreground = ThemeHelper.BrushDangerRed;
                _lblStatus.Text = "エラー: " + ex.Message;
                _btnGenerate.IsEnabled = true;
            }
        }
    }
}
