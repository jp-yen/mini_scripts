// ChallengePromptDialog.cs - TOTP / Access-Challenge 2要素認証対話入力ダイアログ

using System;
using System.Drawing;
using System.Windows.Forms;

namespace RadiusTest
{
    /// <summary>
    /// TOTP / Access-Challenge 2要素認証対話入力ダイアログ
    /// </summary>
    public class ChallengePromptDialog : Form
    {
        private TextBox txtInput = null!;
        private Button btnOk = null!;
        private Button btnCancel = null!;
        public string InputValue => txtInput.Text;

        public ChallengePromptDialog(string promptText)
        {
            this.Text = "追加認証 (Access-Challenge / TOTP)";
            this.Size = new Size(420, 160);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Font = new Font("BIZ UDPゴシック", 9F, FontStyle.Regular, GraphicsUnit.Point);

            var lblPrompt = new Label
            {
                Text = string.IsNullOrWhiteSpace(promptText) ? "ワンタイムパスワード (TOTP) または応答を入力してください:" : promptText,
                Location = new Point(16, 16),
                Size = new Size(370, 36)
            };

            txtInput = new TextBox
            {
                Location = new Point(16, 56),
                Size = new Size(370, 24)
            };

            btnOk = new Button
            {
                Text = "送信 (OK)",
                DialogResult = DialogResult.OK,
                Location = new Point(220, 88),
                Size = new Size(80, 28)
            };

            btnCancel = new Button
            {
                Text = "キャンセル",
                DialogResult = DialogResult.Cancel,
                Location = new Point(306, 88),
                Size = new Size(80, 28)
            };

            this.AcceptButton = btnOk;
            this.CancelButton = btnCancel;

            this.Controls.AddRange(new Control[] { lblPrompt, txtInput, btnOk, btnCancel });
        }
    }
}
