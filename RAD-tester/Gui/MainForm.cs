// MainForm.cs - RADIUS / RadSec クラシックスタイル GUI フロントエンド (.NET 10 Windows Forms)

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace RadiusTest
{
    /// <summary>
    /// 余白の少ないクラシックスタイルのメインフォーム
    /// </summary>
    public partial class MainForm : Form
    {
        // -------------------------------------------------------------
        // コントロール定義
        // -------------------------------------------------------------
        // 接続設定
        private RadioButton rbUdp = null!;
        private RadioButton rbRadSec = null!;
        private TextBox txtServer = null!;
        private NumericUpDown numPort = null!;
        private TextBox txtSecret = null!;
        private ComboBox cmbIpFamily = null!;

        // 認証設定
        private TextBox txtUser = null!;
        private TextBox txtPass = null!;
        private ComboBox cmbAuthMethod = null!;
        private CheckBox chkStatusServer = null!;
        private CheckBox chkMsgAuth = null!;

        // EAP 認証設定
        private ComboBox cmbEap = null!;
        private TextBox txtOuterId = null!;
        private NumericUpDown numEapMtu = null!;
        private ComboBox cmbTtlsInner = null!;

        // TLS・証明書設定
        private ComboBox cmbTlsVer = null!;
        private TextBox txtCertFile = null!;
        private Button btnBrowseCert = null!;
        private TextBox txtKeyFile = null!;
        private Button btnBrowseKey = null!;
        private TextBox txtCertPass = null!;
        private TextBox txtCaCert = null!;
        private Button btnBrowseCa = null!;
        private Button btnSelectStoreCert = null!;
        private CheckBox chkInsecure = null!;
        private CheckBox chkNoNameCheck = null!;

        // オーセンティケーター (NAS) 属性 & 任意追加属性 (トグルボタン、ラベル、入力)
        private Button btnNasId = null!;
        private Label lblNasId = null!;
        private TextBox txtNasId = null!;

        private Button btnNasPortType = null!;
        private Label lblNasPortType = null!;
        private ComboBox cmbNasPortType = null!;

        private Button btnNasPort = null!;
        private Label lblNasPort = null!;
        private NumericUpDown numNasPort = null!;

        private Button btnNasPortId = null!;
        private Label lblNasPortId = null!;
        private TextBox txtNasPortId = null!;

        private Button btnCalledStationId = null!;
        private Label lblCalledStationId = null!;
        private TextBox txtCalledStationId = null!;

        private Button btnNasIp = null!;
        private Label lblNasIp = null!;
        private TextBox txtNasIp = null!;

        private Button btnNasIpv6 = null!;
        private Label lblNasIpv6 = null!;
        private TextBox txtNasIpv6 = null!;

        private TextBox txtCustomAttrs = null!;

        // グループボックス
        private GroupBox grpAuth = null!;
        private GroupBox grpEap = null!;
        private GroupBox grpTls = null!;
        private GroupBox grpNas = null!;
        private GroupBox grpCustomAttrs = null!;

        // アクションボタン
        private Button btnRun = null!;
        private Button btnCopyCli = null!;
        private Button btnClear = null!;

        // 右側タブ
        private TabControl tabResults = null!;
        private ListView lvProgress = null!;
        private TreeView tvPacket = null!;
        private RichTextBox rtbHex = null!;
        private ListBox lbPackets = null!;
        private Label lblPktFlowVal = null!;
        private Label lblPktDetailVal = null!;
        private RichTextBox rtbLog = null!;
        private StatusStrip statusStrip = null!;
        private ToolStripStatusLabel lblStatus = null!;

        // パケット保持リスト
        private readonly List<(RadiusPacket Packet, string Label, string Flow, string Detail)> _capturedPackets = new List<(RadiusPacket, string, string, string)>();
        private bool _isRunning = false;

        public MainForm()
        {
            InitializeComponent();
            SetupEvents();
        }

        private void InitializeComponent()
        {
            this.Text = "RadiusTest - RADIUS & RadSec 認証テストクライアント";
            this.Size = new Size(1160, 740);
            this.MinimumSize = new Size(920, 640);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Font = new Font("BIZ UDPゴシック", 9F, FontStyle.Regular, GraphicsUnit.Point);

            // メインスプリッター (左: 入力設定, 右: 結果・ログ)
            var splitMain = new SplitContainer
            {
                Dock = DockStyle.Fill,
                FixedPanel = FixedPanel.Panel1,
                SplitterWidth = 4
            };

            // ---------------------------------------------------------
            // 左パネル: タブ分離型 設定入力部 + 下部固定アクションボタン
            // ---------------------------------------------------------
            BuildLeftSettingsPanel(splitMain.Panel1);

            var (splitPacket, splitPacketLeft) = BuildResultTabs(splitMain);
            this.Controls.Add(splitMain);

            // フォーム読み込み完了後に安全にスプリッター幅と最小サイズを設定
            this.Load += (s, e) => ConfigureSplitters(splitMain, splitPacket, splitPacketLeft);

            // ステータスバー
            statusStrip = new StatusStrip();
            lblStatus = new ToolStripStatusLabel { Text = "準備完了" };
            statusStrip.Items.Add(lblStatus);
            this.Controls.Add(statusStrip);

            // キーバインド (F5 でテスト開始)
            this.KeyPreview = true;
            this.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.F5 && !_isRunning) RunTest();
            };

            // 初期状態の UI 反映
            UpdateUiState();
        }    }
}
