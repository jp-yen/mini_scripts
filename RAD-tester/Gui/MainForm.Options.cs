// MainForm.Options.cs - UI 入力値 → Options 変換 / CLI 引数文字列生成

using System;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace RadiusTest
{
    public partial class MainForm
    {
        private Options BuildOptionsFromUi()
        {
            var o = new Options
            {
                RadSec = rbRadSec.Checked,
                Server = txtServer.Text.Trim(),
                Port = (int)numPort.Value,
                Secret = txtSecret.Text,
                User = string.IsNullOrWhiteSpace(txtUser.Text) ? null : txtUser.Text.Trim(),
                Pass = string.IsNullOrEmpty(txtPass.Text) ? null : txtPass.Text,
                StatusServer = chkStatusServer.Checked,
                MsgAuth = chkMsgAuth.Checked
            };

            // IP ファミリー
            if (cmbIpFamily.SelectedIndex == 1) o.Ipv6 = true;
            else if (cmbIpFamily.SelectedIndex == 2) o.Ipv4 = true;

            // 認証方式
            o.AuthMethod = cmbAuthMethod.SelectedIndex switch
            {
                1 => "chap",
                2 => "mschapv2",
                _ => "pap"
            };

            // EAP 方式
            if (cmbEap.SelectedIndex > 0)
            {
                o.Eap = cmbEap.SelectedIndex switch
                {
                    1 => "tls",
                    2 => "ttls",
                    3 => "peap",   // PEAPv0
                    4 => "peapv1", // PEAPv1
                    5 => "gtc",
                    6 => "fast",
                    7 => "teap",
                    _ => null
                };
                o.OuterId = string.IsNullOrWhiteSpace(txtOuterId.Text) ? null : txtOuterId.Text.Trim();
                o.TtlsInner = cmbTtlsInner.SelectedIndex switch
                {
                    1 => "mschapv2",
                    2 => "chap",
                    _ => "pap"
                };
                o.EapMtu = (int)numEapMtu.Value;
            }

            // TLS 設定 (TLS使用時のみ反映、未使用時は完全に無視)
            bool isEap = cmbEap.SelectedIndex > 0;
            string? eapType = isEap ? cmbEap.SelectedItem?.ToString() : null;
            bool isGtcPlain = isEap && eapType != null && eapType.StartsWith("EAP-GTC");
            bool usesTls = rbRadSec.Checked || (isEap && !isGtcPlain);

            if (usesTls)
            {
                o.Insecure = chkInsecure.Checked;
                o.NoNameCheck = chkNoNameCheck.Checked;

                o.Tls = cmbTlsVer.SelectedIndex switch
                {
                    1 => "1.3",
                    2 => "1.2",
                    3 => "1.1",
                    4 => "1.0",
                    _ => "auto"
                };

                string certInput = txtCertFile.Text.Trim();
                if (!string.IsNullOrEmpty(certInput))
                {
                    if (certInput.StartsWith("store:", StringComparison.OrdinalIgnoreCase))
                    {
                        var parts = certInput.Split('/');
                        if (parts.Length >= 3) o.Thumb = parts[2];
                    }
                    else
                    {
                        o.CertFile = certInput;
                    }
                }

                if (!string.IsNullOrWhiteSpace(txtKeyFile.Text)) o.KeyFile = txtKeyFile.Text.Trim();
                if (!string.IsNullOrEmpty(txtCertPass.Text)) o.CertPass = txtCertPass.Text;
                if (!string.IsNullOrWhiteSpace(txtCaCert.Text)) o.CaFiles.Add(txtCaCert.Text.Trim());
            }
            else
            {
                o.Insecure = false;
                o.NoNameCheck = false;
                o.CertFile = null;
                o.KeyFile = null;
                o.CertPass = null;
                o.CaFiles.Clear();
                o.Thumb = null;
            }

            // オーセンティケーター (NAS) 属性 (各トグルボタンの状態を判定)
            bool isNasIdOn = (bool)(btnNasId.Tag ?? false);
            bool isNasPortTypeOn = (bool)(btnNasPortType.Tag ?? false);
            bool isNasPortOn = (bool)(btnNasPort.Tag ?? false);
            bool isNasPortIdOn = (bool)(btnNasPortId.Tag ?? false);
            bool isCalledOn = (bool)(btnCalledStationId.Tag ?? false);
            bool isNasIpOn = (bool)(btnNasIp.Tag ?? false);
            bool isNasIpv6On = (bool)(btnNasIpv6.Tag ?? false);

            o.NasId = (isNasIdOn && !string.IsNullOrWhiteSpace(txtNasId.Text)) ? txtNasId.Text.Trim() : null;

            o.NasPortType = null;
            if (isNasPortTypeOn && cmbNasPortType.SelectedIndex > 0)
            {
                string sel = cmbNasPortType.SelectedItem?.ToString() ?? "";
                int colon = sel.IndexOf(':');
                if (colon > 0 && int.TryParse(sel.Substring(0, colon), out int pt))
                {
                    o.NasPortType = pt;
                }
            }

            o.NasPort = isNasPortOn ? (int)numNasPort.Value : (int?)null;
            o.NasPortId = (isNasPortIdOn && !string.IsNullOrWhiteSpace(txtNasPortId.Text)) ? txtNasPortId.Text.Trim() : null;
            o.Called = (isCalledOn && !string.IsNullOrWhiteSpace(txtCalledStationId.Text)) ? txtCalledStationId.Text.Trim() : null;

            // NAS-IP-Address (Type 4: IPv4)
            // ON: 空欄なら null (デフォルト動作: ローカルIPv4自動設定), 入力値があれば上書き
            // OFF: "none" (送信しない)
            o.NasIp = isNasIpOn ? (string.IsNullOrWhiteSpace(txtNasIp.Text) ? null : txtNasIp.Text.Trim()) : "none";

            // NAS-IPv6-Address (Type 95: IPv6)
            // ON: 空欄なら "auto" (デフォルト動作: ローカルIPv6自動設定), 入力値があれば上書き
            // OFF: "none" (送信しない)
            o.NasIpv6 = isNasIpv6On ? (string.IsNullOrWhiteSpace(txtNasIpv6.Text) ? "auto" : txtNasIpv6.Text.Trim()) : "none";

            // 任意追加属性 (カスタム RADIUS 属性)
            if (!string.IsNullOrWhiteSpace(txtCustomAttrs.Text))
            {
                var lines = txtCustomAttrs.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var rawLine in lines)
                {
                    string line = rawLine.Trim();
                    if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;
                    int eq = line.IndexOf('=');
                    if (eq > 0)
                    {
                        string val = line.Substring(eq + 1).Trim();
                        if (val.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                        {
                            o.AttrsHex.Add(line);
                        }
                        else
                        {
                            o.Attrs.Add(line);
                        }
                    }
                }
            }

            o.Finish();
            return o;
        }

        private static string BuildCliCommandString(Options o)
        {
            var sb = new StringBuilder();
            sb.Append("RadiusTest.exe ");
            if (o.RadSec) sb.Append("-radsec ");
            sb.Append($"\"{o.Server}\" ");
            if (!o.RadSec) sb.Append($"\"{o.Secret}\" ");

            if (!o.StatusServer && !string.IsNullOrEmpty(o.User))
            {
                sb.Append($"\"{o.User}\" ");
                if (!string.IsNullOrEmpty(o.Pass)) sb.Append($"\"{o.Pass}\" ");
            }

            if (o.StatusServer) sb.Append("-status ");
            if (o.Port != (o.RadSec ? 2083 : 1812)) sb.Append($"-p {o.Port} ");
            if (o.Ipv6) sb.Append("-6 ");
            if (o.Ipv4) sb.Append("-4 ");
            if (o.AuthMethod != "pap") sb.Append($"-auth {o.AuthMethod} ");
            if (o.Eap != null) sb.Append($"-eap {o.Eap} ");
            if (o.OuterId != null) sb.Append($"-outer \"{o.OuterId}\" ");
            if (o.Eap == "ttls" && o.TtlsInner != "pap") sb.Append($"-ttls-inner {o.TtlsInner} ");
            if (o.Tls != "auto") sb.Append($"-tls {o.Tls} ");
            if (o.CertFile != null) sb.Append($"-cert \"{o.CertFile}\" ");
            if (o.KeyFile != null) sb.Append($"-key \"{o.KeyFile}\" ");
            if (o.Thumb != null) sb.Append($"-thumb \"{o.Thumb}\" ");
            if (o.Insecure) sb.Append("-insecure ");
            if (o.NoNameCheck) sb.Append("-nonamecheck ");
            if (o.NasIp != null && o.NasIp != "none") sb.Append($"-nasip \"{o.NasIp}\" ");
            else if (o.NasIp == "none") sb.Append("-nasip none ");

            if (o.NasIpv6 != null && o.NasIpv6 != "none" && o.NasIpv6 != "auto") sb.Append($"-nasipv6 \"{o.NasIpv6}\" ");
            else if (o.NasIpv6 == "auto") sb.Append("-nasipv6 auto ");

            if (o.NasId != null) sb.Append($"-nasid \"{o.NasId}\" ");
            if (o.NasPort.HasValue) sb.Append($"-nasport {o.NasPort.Value} ");
            if (o.NasPortType.HasValue) sb.Append($"-nasporttype {o.NasPortType.Value} ");
            if (o.NasPortId != null) sb.Append($"-nasportid \"{o.NasPortId}\" ");
            if (o.Called != null) sb.Append($"-called \"{o.Called}\" ");

            foreach (var a in o.Attrs) sb.Append($"-attr \"{a}\" ");
            foreach (var a in o.AttrsHex) sb.Append($"-attrhex \"{a}\" ");

            return sb.ToString().Trim();
        }
    }
}
