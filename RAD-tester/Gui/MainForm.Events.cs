// MainForm.Events.cs - イベント配線 / UI 状態制御

using System;
using System.Drawing;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Windows.Forms;

namespace RadiusTest
{
    public partial class MainForm
    {
        private void SetupEvents()
        {
            rbUdp.CheckedChanged += (s, e) =>
            {
                if (rbUdp.Checked)
                {
                    if (numPort.Value == 2083) numPort.Value = 1812;
                    if (txtSecret.Text == "radsec") txtSecret.Text = "testing123";
                }
                UpdateUiState();
            };

            rbRadSec.CheckedChanged += (s, e) =>
            {
                if (rbRadSec.Checked)
                {
                    if (numPort.Value == 1812) numPort.Value = 2083;
                    if (string.IsNullOrEmpty(txtSecret.Text) || txtSecret.Text == "testing123") txtSecret.Text = "radsec";
                }
                UpdateUiState();
            };

            cmbEap.SelectedIndexChanged += (s, e) => UpdateUiState();
            chkStatusServer.CheckedChanged += (s, e) => UpdateUiState();

            btnBrowseCert.Click += (s, e) =>
            {
                using var ofd = new OpenFileDialog
                {
                    Filter = "証明書ファイル (*.pfx;*.p12;*.crt;*.cer;*.pem)|*.pfx;*.p12;*.crt;*.cer;*.pem|すべてのファイル (*.*)|*.*",
                    Title = "クライアント証明書ファイルを選択"
                };
                if (ofd.ShowDialog(this) == DialogResult.OK) txtCertFile.Text = ofd.FileName;
            };

            btnBrowseKey.Click += (s, e) =>
            {
                using var ofd = new OpenFileDialog
                {
                    Filter = "秘密鍵ファイル (*.key;*.pem)|*.key;*.pem|すべてのファイル (*.*)|*.*",
                    Title = "クライアント秘密鍵ファイルを選択"
                };
                if (ofd.ShowDialog(this) == DialogResult.OK) txtKeyFile.Text = ofd.FileName;
            };

            btnBrowseCa.Click += (s, e) =>
            {
                using var ofd = new OpenFileDialog
                {
                    Filter = "CA 証明書ファイル (*.crt;*.cer;*.pem)|*.crt;*.cer;*.pem|すべてのファイル (*.*)|*.*",
                    Title = "CA 証明書ファイルを選択"
                };
                if (ofd.ShowDialog(this) == DialogResult.OK) txtCaCert.Text = ofd.FileName;
            };

            btnSelectStoreCert.Click += (s, e) =>
            {
                try
                {
                    using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
                    store.Open(OpenFlags.ReadOnly);
                    var certs = X509Certificate2UI.SelectFromCollection(
                        store.Certificates,
                        "クライアント証明書の選択",
                        "RADIUS / RadSec 認証に使用する証明書を選択してください:",
                        X509SelectionFlag.SingleSelection,
                        this.Handle
                    );

                    if (certs.Count > 0)
                    {
                        var c = certs[0];
                        txtCertFile.Text = $"store:CurrentUser/My/{c.Thumbprint}";
                        AppendLog(ConsoleColor.Cyan, $"[証明書ストア] 選択された証明書: {c.Subject} (拇印: {c.Thumbprint})\n");
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, $"証明書ストアの参照に失敗しました: {ex.Message}", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };

            btnRun.Click += (s, e) => { if (!_isRunning) RunTest(); };

            btnClear.Click += (s, e) =>
            {
                lvProgress.Items.Clear();
                _capturedPackets.Clear();
                lbPackets.Items.Clear();
                tvPacket.Nodes.Clear();
                rtbHex.Clear();
                rtbLog.Clear();
                lblPktFlowVal.Text = "-";
                lblPktDetailVal.Text = "-";
                lblStatus.Text = "ログクリア完了";
            };

            btnCopyCli.Click += (s, e) =>
            {
                try
                {
                    var opt = BuildOptionsFromUi();
                    string cli = BuildCliCommandString(opt);
                    Clipboard.SetText(cli);
                    lblStatus.Text = "CLI コマンドをクリップボードにコピーしました。";
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            };

            lbPackets.SelectedIndexChanged += (s, e) =>
            {
                int idx = lbPackets.SelectedIndex;
                if (idx >= 0 && idx < _capturedPackets.Count)
                {
                    var item = _capturedPackets[idx];
                    DisplayPacketDetail(item.Packet, item.Label, item.Flow, item.Detail);
                }
            };
        }

        private void UpdateUiState()
        {
            bool isStatus = chkStatusServer.Checked;
            bool isRadSec = rbRadSec.Checked;
            bool isEap = cmbEap.SelectedIndex > 0;
            string? eapType = isEap ? cmbEap.SelectedItem?.ToString() : null;
            bool eapUsesTls = isEap && (eapType == null || !eapType.StartsWith("EAP-GTC"));
            bool usesTls = isRadSec || eapUsesTls;

            // Status-Server 選択時の排他制御
            txtUser.Enabled = !isStatus;
            txtPass.Enabled = !isStatus;
            cmbAuthMethod.Enabled = !isStatus && !isEap;
            cmbEap.Enabled = !isStatus;
            txtOuterId.Enabled = !isStatus && isEap;
            numEapMtu.Enabled = !isStatus && isEap;
            bool isTtls = isEap && eapType == "EAP-TTLS";
            cmbTtlsInner.Enabled = !isStatus && isTtls;

            // TLS 設定グループのグレーアウト制御
            grpTls.Enabled = usesTls;
            grpTls.Text = usesTls
                ? (isRadSec ? "TLS・クライアント証明書設定 (RadSec)" : $"TLS・証明書設定 ({eapType} トンネル)")
                : "TLS・クライアント証明書設定 (※未使用 - 標準 UDP)";
        }
    }
}
