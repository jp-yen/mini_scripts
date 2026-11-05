// MainForm.Layout.cs - コントロール生成 (設定グループ / 結果タブ / スプリッター)

using System;
using System.Drawing;
using System.Windows.Forms;

namespace RadiusTest
{
    public partial class MainForm
    {
        // 左パネル (設定入力) のレイアウト定数
        private const int GroupMargin = 4;
        private const int GroupWidth = 445;
        private const int LeftPanelWidth = 475;
        private const int LeftPanelMinWidth = 445;
        private const int RightPanelMinWidth = 300;

        /// <summary>
        /// 左パネル: タブコントロール (クライアント設定 / オーセンティケーター属性) と下部固定アクションボタンを構築します。
        /// </summary>
        private void BuildLeftSettingsPanel(SplitterPanel panel)
        {
            // 下部固定アクションボタンパネル
            var pnlAction = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 44,
                Padding = new Padding(4)
            };

            btnRun = new Button
            {
                Text = "認証テスト開始 (F5)",
                Location = new Point(4, 4),
                Size = new Size(185, 36),
                Font = new Font("BIZ UDPゴシック", 10F, FontStyle.Bold, GraphicsUnit.Point),
                BackColor = Color.FromArgb(0, 120, 215),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };

            btnCopyCli = new Button
            {
                Text = "CLI 引数コピー",
                Location = new Point(195, 4),
                Size = new Size(150, 36)
            };

            btnClear = new Button
            {
                Text = "ログクリア",
                Location = new Point(351, 4),
                Size = new Size(105, 36)
            };

            pnlAction.Controls.AddRange(new Control[] { btnRun, btnCopyCli, btnClear });

            // タブコントロール
            var tabSettings = new TabControl
            {
                Dock = DockStyle.Fill
            };

            // タブ 1: クライアント・接続設定
            var tabClient = new TabPage { Text = "認証・接続設定" };
            var pnlClient = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Padding = new Padding(4)
            };
            int curY = 4;
            curY = AddConnectionGroup(pnlClient, curY);
            curY = AddAuthGroup(pnlClient, curY);
            curY = AddEapGroup(pnlClient, curY);
            curY = AddTlsGroup(pnlClient, curY);
            tabClient.Controls.Add(pnlClient);

            // タブ 2: オーセンティケーター属性
            var tabNas = new TabPage { Text = "オーセンティケーター属性" };
            var pnlNas = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Padding = new Padding(4)
            };
            int curNasY = 4;
            curNasY = AddNasGroup(pnlNas, curNasY);
            AddCustomAttrsGroup(pnlNas, curNasY);
            tabNas.Controls.Add(pnlNas);

            tabSettings.TabPages.AddRange(new TabPage[] { tabClient, tabNas });

            // パネルへの配置 (Dock = Bottom を先、Dock = Fill を後に追加)
            panel.Controls.Add(tabSettings);
            panel.Controls.Add(pnlAction);
        }

        /// <summary>
        /// Group 1: サーバー接続設定。次のグループを配置する Y 座標を返します。
        /// </summary>
        private int AddConnectionGroup(Panel pnlLeft, int curY)
        {
            var grpConn = new GroupBox
            {
                Text = "サーバー接続設定",
                Location = new Point(4, curY),
                Size = new Size(GroupWidth, 100),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };

            rbUdp = new RadioButton { Text = "RADIUS (UDP)", Location = new Point(10, 18), Size = new Size(115, 20), Checked = true };
            rbRadSec = new RadioButton { Text = "RadSec (TLS)", Location = new Point(130, 18), Size = new Size(100, 20) };
            cmbIpFamily = new ComboBox
            {
                Location = new Point(245, 17),
                Size = new Size(185, 22),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            cmbIpFamily.Items.AddRange(new object[] { "IP: 自動判別", "IP: IPv6 強制 (-6)", "IP: IPv4 強制 (-4)" });
            cmbIpFamily.SelectedIndex = 0;

            var lblServer = new Label { Text = "サーバー:", Location = new Point(10, 44), AutoSize = true };
            txtServer = new TextBox
            {
                Location = new Point(72, 42),
                Size = new Size(225, 22),
                Text = "127.0.0.1"
            };
            var lblPort = new Label { Text = "ポート:", Location = new Point(307, 44), AutoSize = true };
            numPort = new NumericUpDown
            {
                Location = new Point(355, 42),
                Size = new Size(75, 22),
                Minimum = 1,
                Maximum = 65535,
                Value = 1812
            };

            var lblSecret = new Label { Text = "Secret:", Location = new Point(10, 70), AutoSize = true };
            txtSecret = new TextBox
            {
                Location = new Point(72, 68),
                Size = new Size(358, 22),
                Text = "testing123"
            };

            grpConn.Controls.AddRange(new Control[] {
                rbUdp, rbRadSec, cmbIpFamily,
                lblServer, txtServer, lblPort, numPort,
                lblSecret, txtSecret
            });
            pnlLeft.Controls.Add(grpConn);
            return curY + grpConn.Height + GroupMargin;
        }

        /// <summary>
        /// Group 2: 認証情報 & 方式。次のグループを配置する Y 座標を返します。
        /// </summary>
        private int AddAuthGroup(Panel pnlLeft, int curY)
        {
            grpAuth = new GroupBox
            {
                Text = "認証情報 & 方式",
                Location = new Point(4, curY),
                Size = new Size(GroupWidth, 104),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };

            var lblUser = new Label { Text = "User:", Location = new Point(10, 20), AutoSize = true };
            txtUser = new TextBox
            {
                Location = new Point(72, 18),
                Size = new Size(200, 22),
                Text = "bob"
            };
            var lblMethod = new Label { Text = "方式:", Location = new Point(282, 20), AutoSize = true };
            cmbAuthMethod = new ComboBox
            {
                Location = new Point(325, 18),
                Size = new Size(105, 22),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            cmbAuthMethod.Items.AddRange(new object[] { "PAP (既定)", "CHAP", "MS-CHAPv2" });
            cmbAuthMethod.SelectedIndex = 0;

            var lblPass = new Label { Text = "Pass:", Location = new Point(10, 46), AutoSize = true };
            txtPass = new TextBox
            {
                Location = new Point(72, 44),
                Size = new Size(358, 22),
                Text = "hello"
            };

            chkStatusServer = new CheckBox { Text = "Status-Server (-status)", Location = new Point(10, 72), Size = new Size(195, 20) };
            chkMsgAuth = new CheckBox
            {
                Text = "Msg-Auth 付与 (Type-80)",
                Location = new Point(215, 72),
                Size = new Size(215, 20),
                Checked = true
            };

            grpAuth.Controls.AddRange(new Control[] {
                lblUser, txtUser, lblMethod, cmbAuthMethod,
                lblPass, txtPass,
                chkStatusServer, chkMsgAuth
            });
            pnlLeft.Controls.Add(grpAuth);
            return curY + grpAuth.Height + GroupMargin;
        }

        /// <summary>
        /// Group 3: EAP 認証設定 (802.1X)。次のグループを配置する Y 座標を返します。
        /// </summary>
        private int AddEapGroup(Panel pnlLeft, int curY)
        {
            grpEap = new GroupBox
            {
                Text = "EAP 認証設定 (802.1X)",
                Location = new Point(4, curY),
                Size = new Size(GroupWidth, 102),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };

            var lblEap = new Label { Text = "EAP 方式:", Location = new Point(10, 20), AutoSize = true };
            cmbEap = new ComboBox
            {
                Location = new Point(80, 18),
                Size = new Size(195, 22),
                DropDownStyle = ComboBoxStyle.DropDownList,
                DropDownWidth = 240
            };
            cmbEap.Items.AddRange(new object[] {
                "(なし - 標準 RADIUS)",
                "EAP-TLS",
                "EAP-TTLS",
                "PEAPv0 (MS / MS-CHAPv2)",
                "PEAPv1 (Cisco / EAP-GTC)",
                "EAP-GTC (非トンネル)",
                "EAP-FAST",
                "EAP-TEAP"
            });
            cmbEap.SelectedIndex = 0;

            var lblEapMtu = new Label { Text = "MTU:", Location = new Point(285, 20), AutoSize = true };
            numEapMtu = new NumericUpDown
            {
                Location = new Point(335, 18),
                Size = new Size(95, 22),
                Minimum = 256,
                Maximum = 4096,
                Value = 1000
            };

            var lblOuter = new Label { Text = "Outer Identity:", Location = new Point(10, 46), AutoSize = true };
            txtOuterId = new TextBox
            {
                Location = new Point(116, 44),
                Size = new Size(314, 22),
                Text = "anonymous"
            };

            var lblTtlsInner = new Label { Text = "TTLS 内部認証:", Location = new Point(10, 72), AutoSize = true };
            cmbTtlsInner = new ComboBox
            {
                Location = new Point(116, 70),
                Size = new Size(180, 22),
                DropDownStyle = ComboBoxStyle.DropDownList,
                Enabled = false
            };
            cmbTtlsInner.Items.AddRange(new object[] { "PAP (既定)", "MS-CHAPv2", "CHAP" });
            cmbTtlsInner.SelectedIndex = 0;

            grpEap.Controls.AddRange(new Control[] {
                lblEap, cmbEap, lblEapMtu, numEapMtu,
                lblOuter, txtOuterId,
                lblTtlsInner, cmbTtlsInner
            });
            pnlLeft.Controls.Add(grpEap);
            return curY + grpEap.Height + GroupMargin;
        }

        /// <summary>
        /// Group 4: TLS・クライアント証明書設定。次の要素を配置する Y 座標を返します。
        /// </summary>
        private int AddTlsGroup(Panel pnlLeft, int curY)
        {
            grpTls = new GroupBox
            {
                Text = "TLS・クライアント証明書設定 (RadSec / EAP)",
                Location = new Point(4, curY),
                Size = new Size(GroupWidth, 172),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };

            var lblTlsVer = new Label { Text = "TLS 版:", Location = new Point(10, 20), AutoSize = true };
            cmbTlsVer = new ComboBox
            {
                Location = new Point(72, 18),
                Size = new Size(95, 22),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            cmbTlsVer.Items.AddRange(new object[] { "auto (既定)", "1.3", "1.2", "1.1", "1.0" });
            cmbTlsVer.SelectedIndex = 0;

            btnSelectStoreCert = new Button
            {
                Text = "証明書ストアから選択...",
                Location = new Point(180, 17),
                Size = new Size(250, 24)
            };

            var lblCert = new Label { Text = "証明書:", Location = new Point(10, 46), AutoSize = true };
            txtCertFile = new TextBox
            {
                Location = new Point(88, 44),
                Size = new Size(276, 22)
            };
            btnBrowseCert = new Button
            {
                Text = "参照...",
                Location = new Point(370, 43),
                Size = new Size(60, 24)
            };

            var lblKey = new Label { Text = "秘密鍵:", Location = new Point(10, 72), AutoSize = true };
            txtKeyFile = new TextBox
            {
                Location = new Point(88, 70),
                Size = new Size(276, 22)
            };
            btnBrowseKey = new Button
            {
                Text = "参照...",
                Location = new Point(370, 69),
                Size = new Size(60, 24)
            };

            var lblCertPass = new Label { Text = "パスワード:", Location = new Point(10, 98), AutoSize = true };
            txtCertPass = new TextBox
            {
                Location = new Point(88, 96),
                Size = new Size(342, 22),
                UseSystemPasswordChar = true
            };

            var lblCa = new Label { Text = "CA 証明書:", Location = new Point(10, 124), AutoSize = true };
            txtCaCert = new TextBox
            {
                Location = new Point(88, 122),
                Size = new Size(276, 22)
            };
            btnBrowseCa = new Button
            {
                Text = "参照...",
                Location = new Point(370, 121),
                Size = new Size(60, 24)
            };

            chkInsecure = new CheckBox { Text = "自己署名証明書受入 (-insecure)", Location = new Point(10, 148), Size = new Size(230, 20), Checked = true };
            chkNoNameCheck = new CheckBox
            {
                Text = "ホスト名不一致無視",
                Location = new Point(250, 148),
                Size = new Size(180, 20)
            };

            grpTls.Controls.AddRange(new Control[] {
                lblTlsVer, cmbTlsVer, btnSelectStoreCert,
                lblCert, txtCertFile, btnBrowseCert,
                lblKey, txtKeyFile, btnBrowseKey,
                lblCertPass, txtCertPass,
                lblCa, txtCaCert, btnBrowseCa,
                chkInsecure, chkNoNameCheck
            });
            pnlLeft.Controls.Add(grpTls);
            return curY + grpTls.Height + GroupMargin + 4;
        }

        /// <summary>
        /// オーセンティケーター (NAS) 属性設定グループ。
        /// <summary>
        /// トグルボタンの ON/OFF 状態・カラー・対象コントロールの有効/無効を設定します。
        /// </summary>
        public static void SetToggleState(Button btn, Control ctrl, Label lbl, bool on)
        {
            btn.Tag = on;
            btn.Text = on ? "ON" : "OFF";
            if (on)
            {
                btn.BackColor = Color.FromArgb(220, 245, 225); // ソフトグリーン
                btn.ForeColor = Color.FromArgb(20, 120, 30);
                btn.FlatAppearance.BorderColor = Color.FromArgb(100, 190, 120);
                ctrl.Enabled = true;
                lbl.ForeColor = SystemColors.ControlText;
            }
            else
            {
                btn.BackColor = Color.FromArgb(240, 240, 240); // グレーアウト
                btn.ForeColor = Color.Gray;
                btn.FlatAppearance.BorderColor = Color.FromArgb(210, 210, 210);
                ctrl.Enabled = false;
                lbl.ForeColor = SystemColors.GrayText;
            }
        }

        private Button CreateNasToggleButton(Control ctrl, Label lbl, bool initialOn, string tipText, ToolTip tip)
        {
            var btn = new Button
            {
                Size = new Size(44, 22),
                FlatStyle = FlatStyle.Flat,
                Font = new Font(this.Font.FontFamily, 8f, FontStyle.Bold),
                Cursor = Cursors.Hand,
                Padding = Padding.Empty,
                Margin = Padding.Empty
            };
            btn.FlatAppearance.BorderSize = 1;

            SetToggleState(btn, ctrl, lbl, initialOn);

            btn.Click += (s, e) =>
            {
                bool cur = (bool)(btn.Tag ?? false);
                SetToggleState(btn, ctrl, lbl, !cur);
            };

            tip.SetToolTip(btn, tipText);
            return btn;
        }

        /// <summary>
        /// オーセンティケーター (NAS) 属性グループ。
        /// </summary>
        private int AddNasGroup(Panel pnlNas, int curY)
        {
            grpNas = new GroupBox
            {
                Text = "オーセンティケーター (NAS) 属性設定 (RFC 2865 / 3162 / 3580)",
                Location = new Point(4, curY),
                Size = new Size(GroupWidth, 234),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };

            var tip = new ToolTip
            {
                AutoPopDelay = 8000,
                InitialDelay = 300,
                ReshowDelay = 150,
                ShowAlways = true
            };

            int y = 22;
            int rowH = 28;
            int lblX = 58;
            int lblW = 130;
            int inputX = 192;
            int inputW = 238;

            // 1. NAS-Identifier (Type 32)
            lblNasId = new Label { Text = "NAS-Identifier:", Location = new Point(lblX, y + 2), Size = new Size(lblW, 20) };
            txtNasId = new TextBox { Location = new Point(inputX, y), Size = new Size(inputW, 22) };
            btnNasId = CreateNasToggleButton(txtNasId, lblNasId, false, "クリックでON/OFF切替 (ホスト名など。OFF時は送信されません)", tip);
            txtNasId.TextChanged += (s, e) => { if (!string.IsNullOrEmpty(txtNasId.Text) && !(bool)(btnNasId.Tag ?? false)) SetToggleState(btnNasId, txtNasId, lblNasId, true); };
            tip.SetToolTip(lblNasId, "ホスト名など (OFF時は送信されません)");
            tip.SetToolTip(txtNasId, "ホスト名など");

            // 2. NAS-Port-Type (Type 61)
            y += rowH;
            lblNasPortType = new Label { Text = "NAS-Port-Type:", Location = new Point(lblX, y + 2), Size = new Size(lblW, 20) };
            cmbNasPortType = new ComboBox
            {
                Location = new Point(inputX, y),
                Size = new Size(inputW, 22),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            cmbNasPortType.Items.AddRange(new object[] {
                "(未設定)",
                "19: Wireless - IEEE 802.11 (Wi-Fi)",
                "15: Ethernet (有線LAN)",
                "5: Virtual / VPN",
                "0: Async",
                "1: Sync",
                "2: ISDN Sync",
                "18: SDSL",
                "20: Token Ring",
                "21: FDDI"
            });
            cmbNasPortType.SelectedIndex = 0;
            btnNasPortType = CreateNasToggleButton(cmbNasPortType, lblNasPortType, false, "クリックでON/OFF切替 (接続方法。OFF時は送信されません)", tip);
            cmbNasPortType.SelectedIndexChanged += (s, e) => { if (cmbNasPortType.SelectedIndex > 0 && !(bool)(btnNasPortType.Tag ?? false)) SetToggleState(btnNasPortType, cmbNasPortType, lblNasPortType, true); };
            tip.SetToolTip(lblNasPortType, "接続方法 (OFF時は送信されません)");
            tip.SetToolTip(cmbNasPortType, "接続方法");

            // 3. NAS-Port (Type 5)
            y += rowH;
            lblNasPort = new Label { Text = "NAS-Port:", Location = new Point(lblX, y + 2), Size = new Size(lblW, 20) };
            numNasPort = new NumericUpDown
            {
                Location = new Point(inputX, y),
                Size = new Size(120, 22),
                Minimum = 0,
                Maximum = 65535,
                Value = 0
            };
            btnNasPort = CreateNasToggleButton(numNasPort, lblNasPort, false, "クリックでON/OFF切替 (論理ポート番号。OFF時は送信されません)", tip);
            tip.SetToolTip(lblNasPort, "論理インターフェース番号 (OFF時は送信されません)");
            tip.SetToolTip(numNasPort, "論理インターフェース番号");

            // 4. NAS-Port-Id (Type 87)
            y += rowH;
            lblNasPortId = new Label { Text = "NAS-Port-Id:", Location = new Point(lblX, y + 2), Size = new Size(lblW, 20) };
            txtNasPortId = new TextBox { Location = new Point(inputX, y), Size = new Size(inputW, 22) };
            btnNasPortId = CreateNasToggleButton(txtNasPortId, lblNasPortId, false, "クリックでON/OFF切替 (ポート名、SSID。OFF時は送信されません)", tip);
            txtNasPortId.TextChanged += (s, e) => { if (!string.IsNullOrEmpty(txtNasPortId.Text) && !(bool)(btnNasPortId.Tag ?? false)) SetToggleState(btnNasPortId, txtNasPortId, lblNasPortId, true); };
            tip.SetToolTip(lblNasPortId, "ポート名、SSID (OFF時は送信されません)");
            tip.SetToolTip(txtNasPortId, "ポート名、SSID");

            // 5. Called-Station-Id (Type 30)
            y += rowH;
            lblCalledStationId = new Label { Text = "Called-Station-Id:", Location = new Point(lblX, y + 2), Size = new Size(lblW, 20) };
            txtCalledStationId = new TextBox { Location = new Point(inputX, y), Size = new Size(inputW, 22) };
            btnCalledStationId = CreateNasToggleButton(txtCalledStationId, lblCalledStationId, false, "クリックでON/OFF切替 (APのMAC:SSIDなど。OFF時は送信されません)", tip);
            txtCalledStationId.TextChanged += (s, e) => { if (!string.IsNullOrEmpty(txtCalledStationId.Text) && !(bool)(btnCalledStationId.Tag ?? false)) SetToggleState(btnCalledStationId, txtCalledStationId, lblCalledStationId, true); };
            tip.SetToolTip(lblCalledStationId, "APのMAC:SSID など (OFF時は送信されません)");
            tip.SetToolTip(txtCalledStationId, "APのMAC:SSID など");

            // 6. NAS-IP-Address (Type 4: IPv4)
            y += rowH;
            lblNasIp = new Label { Text = "NAS-IP-Address:", Location = new Point(lblX, y + 2), Size = new Size(lblW, 20) };
            txtNasIp = new TextBox { Location = new Point(inputX, y), Size = new Size(inputW, 22) };
            btnNasIp = CreateNasToggleButton(txtNasIp, lblNasIp, true, "クリックでON/OFF切替 (空欄時はローカルIPv4自動設定、OFF時は送信されません)", tip);
            txtNasIp.TextChanged += (s, e) => { if (!string.IsNullOrEmpty(txtNasIp.Text) && !(bool)(btnNasIp.Tag ?? false)) SetToggleState(btnNasIp, txtNasIp, lblNasIp, true); };
            tip.SetToolTip(lblNasIp, "IPv4 NAS アドレス。空欄時はローカルIPv4自動設定 (OFF時は送信されません)");
            tip.SetToolTip(txtNasIp, "IPv4 NAS アドレス。空欄時はローカルIPv4自動設定");

            // 7. NAS-IPv6-Address (Type 95: IPv6)
            y += rowH;
            lblNasIpv6 = new Label { Text = "NAS-IPv6-Address:", Location = new Point(lblX, y + 2), Size = new Size(lblW, 20) };
            txtNasIpv6 = new TextBox { Location = new Point(inputX, y), Size = new Size(inputW, 22) };
            btnNasIpv6 = CreateNasToggleButton(txtNasIpv6, lblNasIpv6, false, "クリックでON/OFF切替 (空欄時はローカルIPv6自動設定、OFF時は送信されません)", tip);
            txtNasIpv6.TextChanged += (s, e) => { if (!string.IsNullOrEmpty(txtNasIpv6.Text) && !(bool)(btnNasIpv6.Tag ?? false)) SetToggleState(btnNasIpv6, txtNasIpv6, lblNasIpv6, true); };
            tip.SetToolTip(lblNasIpv6, "IPv6 NAS アドレス。空欄時はローカルIPv6自動設定 (OFF時は送信されません)");
            tip.SetToolTip(txtNasIpv6, "IPv6 NAS アドレス。空欄時はローカルIPv6自動設定");

            // ボタンの Y 位置を設定
            btnNasId.Location = new Point(10, 22);
            btnNasPortType.Location = new Point(10, 22 + rowH);
            btnNasPort.Location = new Point(10, 22 + rowH * 2);
            btnNasPortId.Location = new Point(10, 22 + rowH * 3);
            btnCalledStationId.Location = new Point(10, 22 + rowH * 4);
            btnNasIp.Location = new Point(10, 22 + rowH * 5);
            btnNasIpv6.Location = new Point(10, 22 + rowH * 6);

            grpNas.Controls.AddRange(new Control[] {
                btnNasId, lblNasId, txtNasId,
                btnNasPortType, lblNasPortType, cmbNasPortType,
                btnNasPort, lblNasPort, numNasPort,
                btnNasPortId, lblNasPortId, txtNasPortId,
                btnCalledStationId, lblCalledStationId, txtCalledStationId,
                btnNasIp, lblNasIp, txtNasIp,
                btnNasIpv6, lblNasIpv6, txtNasIpv6
            });

            pnlNas.Controls.Add(grpNas);
            return curY + grpNas.Height + GroupMargin;
        }

        /// <summary>
        /// 任意追加属性 (カスタム属性) グループ。
        /// </summary>
        private int AddCustomAttrsGroup(Panel pnlNas, int curY)
        {
            grpCustomAttrs = new GroupBox
            {
                Text = "任意追加属性 (カスタム RADIUS 属性)",
                Location = new Point(4, curY),
                Size = new Size(GroupWidth, 180),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };

            var tip = new ToolTip
            {
                AutoPopDelay = 8000,
                InitialDelay = 300,
                ReshowDelay = 150,
                ShowAlways = true
            };

            var lblDesc = new Label
            {
                Text = "1行に1属性を入力: Name=値 または Type=値 (16進数は 0x...)\n例: Reply-Message=Hello / 26=311:1:val / 1=testuser",
                Location = new Point(10, 18),
                Size = new Size(425, 28),
                ForeColor = Color.FromArgb(80, 80, 80)
            };

            txtCustomAttrs = new TextBox
            {
                Location = new Point(10, 48),
                Size = new Size(425, 120),
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                Font = new Font("Consolas", 9F, FontStyle.Regular, GraphicsUnit.Point)
            };

            tip.SetToolTip(txtCustomAttrs, "1行に1属性を指定してください (例: 32=host, Reply-Message=msg, 26=0xHEX)");

            grpCustomAttrs.Controls.AddRange(new Control[] { lblDesc, txtCustomAttrs });
            pnlNas.Controls.Add(grpCustomAttrs);
            return curY + grpCustomAttrs.Height + GroupMargin;
        }

        /// <summary>
        /// 右パネル: 結果タブ (TabControl) を構築します。パケットタブ内のスプリッターを返します。
        /// </summary>
        private (SplitContainer splitPacket, SplitContainer splitPacketLeft) BuildResultTabs(SplitContainer splitMain)
        {
            tabResults = new TabControl { Dock = DockStyle.Fill };

            var tabProgress = BuildProgressTab();
            var tabPacket = BuildPacketTab(out var splitPacket, out var splitPacketLeft);
            var tabLog = BuildLogTab();

            tabResults.TabPages.AddRange(new[] { tabProgress, tabPacket, tabLog });
            splitMain.Panel2.Controls.Add(tabResults);
            return (splitPacket, splitPacketLeft);
        }

        /// <summary>
        /// タブ 1: プロトコル進行 (ListView)
        /// </summary>
        private TabPage BuildProgressTab()
        {
            var tabProgress = new TabPage { Text = "プロトコル進行 (Flow)" };
            lvProgress = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                Font = new Font("BIZ UDPゴシック", 9F, FontStyle.Regular, GraphicsUnit.Point)
            };
            lvProgress.Columns.Add("#", 32, HorizontalAlignment.Right);
            lvProgress.Columns.Add("フェーズ", 70, HorizontalAlignment.Left);
            lvProgress.Columns.Add("状態", 50, HorizontalAlignment.Center);
            lvProgress.Columns.Add("通信方向 (Flow)", 160, HorizontalAlignment.Left);
            lvProgress.Columns.Add("時間 (ms)", 65, HorizontalAlignment.Right);
            lvProgress.Columns.Add("詳細メッセージ", 360, HorizontalAlignment.Left);
            tabProgress.Controls.Add(lvProgress);
            return tabProgress;
        }

        /// <summary>
        /// タブ 2: パケット属性ツリー & HEX ダンプ
        /// </summary>
        private TabPage BuildPacketTab(out SplitContainer splitPacket, out SplitContainer splitPacketLeft)
        {
            var tabPacket = new TabPage { Text = "パケット属性 & HEX" };
            splitPacket = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterWidth = 4
            };

            // 左側: 送受信パケット一覧 (上) と RADIUS 属性一覧ツリー (下) を可動分割するスプリッター
            splitPacketLeft = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterWidth = 4
            };

            var lblPktList = new Label { Text = "送受信パケット一覧:", Dock = DockStyle.Top, Height = 20, TextAlign = ContentAlignment.BottomLeft };
            lbPackets = new ListBox { Dock = DockStyle.Fill };
            splitPacketLeft.Panel1.Controls.Add(lbPackets);
            splitPacketLeft.Panel1.Controls.Add(lblPktList);

            var lblPktTree = new Label { Text = "RADIUS 属性一覧ツリー:", Dock = DockStyle.Top, Height = 20, TextAlign = ContentAlignment.BottomLeft };
            tvPacket = new TreeView { Dock = DockStyle.Fill };

            // 選択パケットの向きと詳細メッセージを表示する情報パネル（送受信パケット一覧と RADIUS 属性一覧ツリーの間に配置）
            var pnlPktMeta = new Panel
            {
                Dock = DockStyle.Top,
                Height = 44,
                BackColor = Color.FromArgb(248, 249, 250),
                BorderStyle = BorderStyle.FixedSingle,
                Padding = new Padding(4)
            };
            var lblFlowHdr = new Label { Text = "向き:", Location = new Point(6, 4), Size = new Size(45, 16), Font = new Font(this.Font, FontStyle.Bold), ForeColor = Color.FromArgb(60, 60, 60) };
            lblPktFlowVal = new Label { Text = "-", Location = new Point(96, 4), AutoSize = true, Font = new Font(this.Font, FontStyle.Bold), ForeColor = Color.FromArgb(0, 90, 180) };
            var lblDetailHdr = new Label { Text = "詳細メッセージ:", Location = new Point(6, 22), Size = new Size(88, 16), Font = new Font(this.Font, FontStyle.Bold), ForeColor = Color.FromArgb(60, 60, 60) };
            lblPktDetailVal = new Label { Text = "-", Location = new Point(96, 22), AutoSize = true, ForeColor = Color.FromArgb(30, 30, 30) };
            pnlPktMeta.Controls.AddRange(new Control[] { lblFlowHdr, lblPktFlowVal, lblDetailHdr, lblPktDetailVal });

            splitPacketLeft.Panel2.Controls.Add(tvPacket);
            splitPacketLeft.Panel2.Controls.Add(lblPktTree);
            splitPacketLeft.Panel2.Controls.Add(pnlPktMeta);

            var pnlPacketRight = new Panel { Dock = DockStyle.Fill };
            var lblHex = new Label { Text = "RAW 16進ダンプ (HEX Preview):", Dock = DockStyle.Top, Height = 20, TextAlign = ContentAlignment.BottomLeft };
            rtbHex = new RichTextBox
            {
                Dock = DockStyle.Fill,
                Font = new Font("BIZ UDゴシック", 9F, FontStyle.Regular, GraphicsUnit.Point),
                ReadOnly = true,
                BackColor = Color.FromArgb(250, 250, 250),
                WordWrap = false
            };
            pnlPacketRight.Controls.Add(rtbHex);
            pnlPacketRight.Controls.Add(lblHex);

            splitPacket.Panel1.Controls.Add(splitPacketLeft);
            splitPacket.Panel2.Controls.Add(pnlPacketRight);
            tabPacket.Controls.Add(splitPacket);
            return tabPacket;
        }

        /// <summary>
        /// タブ 3: ログコンソール (RichTextBox)
        /// </summary>
        private TabPage BuildLogTab()
        {
            var tabLog = new TabPage { Text = "実行ログ (Console Log)" };
            rtbLog = new RichTextBox
            {
                Dock = DockStyle.Fill,
                Font = new Font("BIZ UDゴシック", 9F, FontStyle.Regular, GraphicsUnit.Point),
                ReadOnly = true,
                BackColor = Color.FromArgb(250, 250, 250),
                ForeColor = Color.FromArgb(30, 30, 30),
                WordWrap = true
            };
            tabLog.Controls.Add(rtbLog);
            return tabLog;
        }

        /// <summary>
        /// フォーム読み込み完了後 (幅が確定してから) にスプリッター幅と最小サイズを設定します。
        /// ※ 初期化子内で指定すると、幅未確定のため InvalidOperationException になるため Load 後に適用。
        /// </summary>
        private void ConfigureSplitters(SplitContainer splitMain, SplitContainer splitPacket, SplitContainer splitPacketLeft)
        {
            try
            {
                if (splitMain.Width > 500)
                {
                    splitMain.SplitterDistance = LeftPanelWidth;
                }
                splitMain.Panel1MinSize = LeftPanelMinWidth;
                splitMain.Panel2MinSize = RightPanelMinWidth;

                if (splitPacket.Width > 300)
                {
                    splitPacket.SplitterDistance = Math.Min(340, Math.Max(150, splitPacket.Width / 2));
                }

                if (splitPacketLeft.Height > 200)
                {
                    splitPacketLeft.SplitterDistance = Math.Min(150, Math.Max(60, splitPacketLeft.Height / 3));
                }
                splitPacketLeft.Panel1MinSize = 50;
                splitPacketLeft.Panel2MinSize = 80;
            }
            catch { }
        }
    }
}
