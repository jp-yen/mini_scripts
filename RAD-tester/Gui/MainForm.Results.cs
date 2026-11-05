// MainForm.Results.cs - 実行結果の表示 (進行リスト / パケット詳細 / ログ)

using System;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace RadiusTest
{
    public partial class MainForm
    {
        private void AddProgressRow(ProgressEntry e)
        {
            var item = new ListViewItem((lvProgress.Items.Count + 1).ToString());
            item.SubItems.Add(e.Phase);
            item.SubItems.Add(e.State.ToString());
            item.SubItems.Add(string.IsNullOrEmpty(e.Flow) ? "-" : e.Flow);
            item.SubItems.Add(e.ElapsedMs.ToString());
            item.SubItems.Add(e.Msg);

            if (e.State == PState.Fail)
            {
                item.BackColor = Color.FromArgb(255, 235, 235); // 失敗: 淡い赤
            }
            else if (e.State == PState.Warn)
            {
                item.BackColor = Color.FromArgb(255, 255, 225); // 警告: 淡い黄
            }
            else if (e.State == PState.Skip)
            {
                item.BackColor = Color.FromArgb(245, 245, 245); // スキップ: 淡いグレー
            }
            else
            {
                // データ準備 / 送信 / 受信 / 結果・検証の色分け
                bool isBuild = e.Phase == "BUILD" || e.Msg.Contains("準備") || e.Msg.Contains("構築") || e.Msg.Contains("開始");
                bool isSend = e.Phase == "SEND" || (e.Flow != null && e.Flow.Contains("-->")) || e.Msg.Contains("送信") || e.Msg.Contains("返信");
                bool isRecv = e.Phase == "RECV" || (e.Flow != null && e.Flow.Contains("<--")) || e.Msg.Contains("受信");

                if (e.Phase == "BUILD" || (isBuild && !isSend && !isRecv))
                {
                    item.BackColor = Color.FromArgb(246, 242, 254); // 準備: 淡いラベンダー/パープル
                }
                else if (e.Phase == "SEND" || isSend)
                {
                    item.BackColor = Color.FromArgb(232, 244, 254); // 送信: 淡いブルー
                }
                else if (e.Phase == "RECV" || isRecv)
                {
                    item.BackColor = Color.FromArgb(234, 252, 234); // 受信: 淡いグリーン
                }
                else if (e.Phase == "RESULT" || e.Phase == "VERIFY")
                {
                    item.BackColor = Color.FromArgb(240, 255, 244); // 結果・検証: 淡いミントグリーン
                }
                else
                {
                    item.BackColor = Color.White;
                }
            }

            lvProgress.Items.Add(item);
            item.EnsureVisible();
        }

        private void DisplayPacketDetail(RadiusPacket p, string label, string flow, string detail)
        {
            lblPktFlowVal.Text = string.IsNullOrEmpty(flow) ? "-" : flow;
            lblPktDetailVal.Text = string.IsNullOrEmpty(detail) ? label : detail;

            tvPacket.BeginUpdate();
            tvPacket.Nodes.Clear();

            var root = tvPacket.Nodes.Add($"パケット: {label} ({p.Raw.Length} バイト)");
            root.Nodes.Add($"コード (Code): {p.Code} ({Dict.CodeName(p.Code)})");
            root.Nodes.Add($"識別子 (ID): {p.Identifier}");
            root.Nodes.Add($"パケット長: {p.Raw.Length}");
            root.Nodes.Add($"Authenticator: {Printer.ToHex(p.Authenticator)}");

            var attrRoot = root.Nodes.Add($"属性一覧 ({p.Attrs.Count} 件)");
            foreach (var a in p.Attrs)
            {
                string val = Printer.FormatValue(a.Type, a.Value);
                attrRoot.Nodes.Add($"[{a.Type,3}] {Dict.Name(a.Type)} ({a.Value.Length} バイト) = {val}");
            }

            root.ExpandAll();
            tvPacket.EndUpdate();

            // HEX ダンプの生成
            rtbHex.Text = GenerateHexDumpText(p.Raw);
        }

        private static string GenerateHexDumpText(byte[] data)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < data.Length; i += 16)
            {
                sb.AppendFormat("{0:x4}  ", i);
                for (int j = 0; j < 16; j++)
                {
                    if (i + j < data.Length) sb.AppendFormat("{0:x2} ", data[i + j]);
                    else sb.Append("   ");
                    if (j == 7) sb.Append(' ');
                }
                sb.Append(" |");
                for (int j = 0; j < 16 && (i + j) < data.Length; j++)
                {
                    byte b = data[i + j];
                    sb.Append(b >= 32 && b <= 126 ? (char)b : '.');
                }
                sb.AppendLine("|");
            }
            return sb.ToString();
        }

        public void AppendLog(ConsoleColor c, string text)
        {
            if (this.IsDisposed || !this.IsHandleCreated) return;
            this.BeginInvoke((MethodInvoker)(() =>
            {
                Color fc = c switch
                {
                    ConsoleColor.Green => Color.FromArgb(0, 128, 0),       // 濃い緑 (Ok)
                    ConsoleColor.Red => Color.FromArgb(192, 0, 0),          // 濃い赤 (Fail/エラー)
                    ConsoleColor.Yellow => Color.FromArgb(160, 110, 0),     // アンバー/茶黄 (Warn)
                    ConsoleColor.Cyan => Color.FromArgb(0, 110, 150),       // 落ち着いた青緑/シアン (Info)
                    ConsoleColor.Magenta => Color.FromArgb(136, 0, 136),    // 濃いマゼンタ
                    ConsoleColor.DarkGray => Color.FromArgb(120, 120, 120), // グレー (補足情報)
                    ConsoleColor.Gray => Color.FromArgb(80, 80, 80),        // 標準テキスト
                    _ => Color.FromArgb(30, 30, 30)                         // 通常テキスト (濃いグレー/黒)
                };
                rtbLog.SelectionStart = rtbLog.TextLength;
                rtbLog.SelectionLength = 0;
                rtbLog.SelectionColor = fc;
                rtbLog.AppendText(text);
                rtbLog.ScrollToCaret();
            }));
        }

        private class GuiOutputSink : IOutput
        {
            private readonly MainForm _form;
            public GuiOutputSink(MainForm form) { _form = form; }

            public void Write(bool detail, Seg[] line)
            {
                var sb = new StringBuilder();
                foreach (var s in line)
                {
                    _form.AppendLog(s.C, s.T);
                }
                _form.AppendLog(ConsoleColor.Gray, "\n");
            }
        }
    }
}
