// MainForm.Run.cs - テスト実行 (非同期) とプロンプト連携

using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace RadiusTest
{
    public partial class MainForm
    {
        private async void RunTest()
        {
            Options opt;
            try
            {
                opt = BuildOptionsFromUi();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "設定エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _isRunning = true;
            btnRun.Enabled = false;
            btnRun.Text = "テスト実行中...";
            lblStatus.Text = "RADIUS 認証テストを実行中...";

            // UI 状態の初期化
            lvProgress.Items.Clear();
            _capturedPackets.Clear();
            lbPackets.Items.Clear();
            tvPacket.Nodes.Clear();
            rtbHex.Clear();
            rtbLog.Clear();
            lblPktFlowVal.Text = "-";
            lblPktDetailVal.Text = "-";

            // Progress と Printer のイベント購読
            Action<ProgressEntry> onProg = entry =>
            {
                if (this.IsDisposed || !this.IsHandleCreated) return;
                this.BeginInvoke((MethodInvoker)(() =>
                {
                    AddProgressRow(entry);
                }));
            };

            Action<RadiusPacket, string> onPkt = (pkt, lbl) =>
            {
                if (this.IsDisposed || !this.IsHandleCreated) return;
                this.BeginInvoke((MethodInvoker)(() =>
                {
                    string flow = "-";
                    string action = "";
                    int closeBracket = lbl.IndexOf(']');
                    if (lbl.StartsWith("[") && closeBracket > 1)
                    {
                        flow = lbl.Substring(1, closeBracket - 1).Trim();
                        string rest = lbl.Substring(closeBracket + 1).Trim();
                        if (rest.StartsWith("送信")) action = "送信 ";
                        else if (rest.StartsWith("受信")) action = "受信 ";
                    }

                    string summary = PacketAnalyzer.Summarize(pkt);
                    string detail = string.IsNullOrEmpty(action) ? summary : $"{action}{summary}";
                    string listDisplay = $"[{_capturedPackets.Count + 1}] [{flow}] {detail} ({pkt.Raw.Length} バイト)";

                    _capturedPackets.Add((pkt, listDisplay, flow, detail));
                    lbPackets.Items.Add(listDisplay);
                    if (lbPackets.SelectedIndex < 0) lbPackets.SelectedIndex = 0;
                }));
            };

            // 出力シンクのフック
            var oldSink = Out.Sink;
            var oldPromptFunc = Out.PromptFunc;

            Out.Sink = new GuiOutputSink(this);

            // TOTP / Access-Challenge 2要素認証用対話プロンプト
            Out.PromptFunc = (promptText) =>
            {
                if (this.IsDisposed || !this.IsHandleCreated) return null;
                string? input = null;
                this.Invoke((MethodInvoker)(() =>
                {
                    using var dlg = new ChallengePromptDialog(promptText);
                    if (dlg.ShowDialog(this) == DialogResult.OK)
                    {
                        input = dlg.InputValue;
                        AppendLog(ConsoleColor.Yellow, $"[2要素認証/TOTP] チャレンジ応答を入力しました。\n");
                    }
                }));
                return input;
            };

            Progress.OnProgress += onProg;
            Printer.OnPacketInspected += onPkt;

            int rc = -1;
            await Task.Run(() =>
            {
                try
                {
                    rc = Runner.Run(opt);
                }
                catch (Exception ex)
                {
                    AppendLog(ConsoleColor.Red, $"\n例外エラー: {ex.Message}\n");
                }
            });

            Progress.OnProgress -= onProg;
            Printer.OnPacketInspected -= onPkt;
            Out.Sink = oldSink;
            Out.PromptFunc = oldPromptFunc;

            _isRunning = false;
            btnRun.Enabled = true;
            btnRun.Text = "認証テスト開始 (F5)";

            string resText = rc == 0 ? "認証成功 (Access-Accept)" :
                             rc == 1 ? "認証拒否 (Access-Reject)" :
                             rc == 2 ? "パラメータエラー" : "通信エラー・未完了";
            lblStatus.Text = $"完了: {resText} (終了コード: {rc})";
        }
    }
}
