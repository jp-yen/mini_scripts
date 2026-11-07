// --------------------------------------------------------------------------------------------------------------------
// <copyright file="SettingsForm.cs" company="CPU Thermo Watcher Contributors">
//   Copyright (c) 2026, CPU Thermo Watcher Contributors. All rights reserved.
//   Licensed under the 3-Clause BSD License.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace CpuThermoWatcher
{
    public class SettingsForm : Form
    {
        private Settings _settings;
        private CpuTemperatureSnapshot _snapshot;

        private NumericUpDown _numCpuThreshold;
        private NumericUpDown _numIGpuThreshold;
        private NumericUpDown _numDGpuThreshold;
        private NumericUpDown _numInterval;
        private Dictionary<string, CheckBox> _deviceCheckBoxes = new Dictionary<string, CheckBox>();
        private CheckBox _chkEventLog;
        private NumericUpDown _numEventLogInterval;
        private ComboBox _cmbTextColor;

        public SettingsForm(Settings settings, CpuTemperatureSnapshot snapshot = null)
        {
            _settings = settings;
            _snapshot = snapshot;
            InitializeComponent();
            LoadValues();
        }

        private void InitializeComponent()
        {
            this.Text = "CPU / GPU Thermo Watcher 設定";
            this.Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.StartPosition = FormStartPosition.CenterScreen;

            // DPI スケールの動的計算
            float dpiScale = UiHelper.GetDpiScale(this);

            this.ClientSize = new Size((int)(620 * dpiScale), (int)(520 * dpiScale));
            this.Padding = new Padding((int)(18 * dpiScale));

            TableLayoutPanel mainLayout = new TableLayoutPanel();
            mainLayout.Dock = DockStyle.Fill;
            mainLayout.AutoSize = true;
            mainLayout.ColumnCount = 2;
            mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            int row = 0;

            // 1. タスクトレイ表示対象デバイスのグループ
            Label lblTrayTarget = new Label();
            lblTrayTarget.Text = "タスクトレイ表示対象:";
            lblTrayTarget.AutoSize = true;
            lblTrayTarget.Dock = DockStyle.Fill;
            lblTrayTarget.TextAlign = ContentAlignment.MiddleLeft;
            lblTrayTarget.Margin = new Padding(0, 0, (int)(15 * dpiScale), (int)(10 * dpiScale));

            FlowLayoutPanel trayCheckPanel = new FlowLayoutPanel();
            trayCheckPanel.Dock = DockStyle.Fill;
            trayCheckPanel.AutoSize = true;
            trayCheckPanel.FlowDirection = FlowDirection.TopDown;
            trayCheckPanel.WrapContents = false;
            trayCheckPanel.Margin = new Padding(0, 0, 0, (int)(10 * dpiScale));

            // デバイス一覧からチェックボックスを動的生成
            List<HardwareDeviceSummary> deviceList = (_snapshot != null && _snapshot.Devices != null && _snapshot.Devices.Count > 0)
                ? _snapshot.Devices
                : GetDefaultDeviceList();

            foreach (HardwareDeviceSummary dev in deviceList)
            {
                CheckBox chk = new CheckBox();
                chk.Text = dev.DisplayName;
                chk.AutoSize = true;
                chk.Tag = dev.DeviceId;
                chk.Margin = new Padding(0, (int)(2 * dpiScale), 0, (int)(4 * dpiScale));
                _deviceCheckBoxes[dev.DeviceId] = chk;
                trayCheckPanel.Controls.Add(chk);
            }

            mainLayout.Controls.Add(lblTrayTarget, 0, row);
            mainLayout.Controls.Add(trayCheckPanel, 1, row);
            row++;

            // 2. 警告しきい値 (CPU)
            Label lblCpuThreshold = new Label();
            lblCpuThreshold.Text = "CPU 高温警告しきい値 (℃):";
            lblCpuThreshold.AutoSize = true;
            lblCpuThreshold.Dock = DockStyle.Fill;
            lblCpuThreshold.TextAlign = ContentAlignment.MiddleLeft;
            lblCpuThreshold.Margin = new Padding(0, 0, (int)(15 * dpiScale), (int)(8 * dpiScale));

            _numCpuThreshold = new NumericUpDown();
            _numCpuThreshold.Minimum = 40;
            _numCpuThreshold.Maximum = 115;
            _numCpuThreshold.DecimalPlaces = 0;
            _numCpuThreshold.Width = (int)(110 * dpiScale);
            _numCpuThreshold.Margin = new Padding(0, 0, 0, (int)(8 * dpiScale));

            mainLayout.Controls.Add(lblCpuThreshold, 0, row);
            mainLayout.Controls.Add(_numCpuThreshold, 1, row);
            row++;

            // 3. 警告しきい値 (iGPU)
            Label lblIGpuThreshold = new Label();
            lblIGpuThreshold.Text = "iGPU 高温警告しきい値 (℃):";
            lblIGpuThreshold.AutoSize = true;
            lblIGpuThreshold.Dock = DockStyle.Fill;
            lblIGpuThreshold.TextAlign = ContentAlignment.MiddleLeft;
            lblIGpuThreshold.Margin = new Padding(0, 0, (int)(15 * dpiScale), (int)(8 * dpiScale));

            _numIGpuThreshold = new NumericUpDown();
            _numIGpuThreshold.Minimum = 40;
            _numIGpuThreshold.Maximum = 115;
            _numIGpuThreshold.DecimalPlaces = 0;
            _numIGpuThreshold.Width = (int)(110 * dpiScale);
            _numIGpuThreshold.Margin = new Padding(0, 0, 0, (int)(8 * dpiScale));

            mainLayout.Controls.Add(lblIGpuThreshold, 0, row);
            mainLayout.Controls.Add(_numIGpuThreshold, 1, row);
            row++;

            // 4. 警告しきい値 (dGPU)
            Label lblDGpuThreshold = new Label();
            lblDGpuThreshold.Text = "dGPU 高温警告しきい値 (℃):";
            lblDGpuThreshold.AutoSize = true;
            lblDGpuThreshold.Dock = DockStyle.Fill;
            lblDGpuThreshold.TextAlign = ContentAlignment.MiddleLeft;
            lblDGpuThreshold.Margin = new Padding(0, 0, (int)(15 * dpiScale), (int)(10 * dpiScale));

            _numDGpuThreshold = new NumericUpDown();
            _numDGpuThreshold.Minimum = 40;
            _numDGpuThreshold.Maximum = 115;
            _numDGpuThreshold.DecimalPlaces = 0;
            _numDGpuThreshold.Width = (int)(110 * dpiScale);
            _numDGpuThreshold.Margin = new Padding(0, 0, 0, (int)(10 * dpiScale));

            mainLayout.Controls.Add(lblDGpuThreshold, 0, row);
            mainLayout.Controls.Add(_numDGpuThreshold, 1, row);
            row++;

            // 5. 更新間隔
            Label lblInterval = new Label();
            lblInterval.Text = "温度更新の間隔 (秒):";
            lblInterval.AutoSize = true;
            lblInterval.Dock = DockStyle.Fill;
            lblInterval.TextAlign = ContentAlignment.MiddleLeft;
            lblInterval.Margin = new Padding(0, 0, (int)(15 * dpiScale), (int)(10 * dpiScale));

            _numInterval = new NumericUpDown();
            _numInterval.Minimum = 1;
            _numInterval.Maximum = 60;
            _numInterval.Width = (int)(110 * dpiScale);
            _numInterval.Margin = new Padding(0, 0, 0, (int)(10 * dpiScale));

            mainLayout.Controls.Add(lblInterval, 0, row);
            mainLayout.Controls.Add(_numInterval, 1, row);
            row++;

            // 6. イベントログ記録フラグ
            _chkEventLog = new CheckBox();
            _chkEventLog.Text = "Windows イベントログに温度を記録する (CPU, iGPU, dGPU)";
            _chkEventLog.AutoSize = true;
            _chkEventLog.Margin = new Padding(0, (int)(2 * dpiScale), 0, (int)(6 * dpiScale));
            _chkEventLog.CheckedChanged += delegate
            {
                _numEventLogInterval.Enabled = _chkEventLog.Checked;
            };

            mainLayout.Controls.Add(_chkEventLog, 0, row);
            mainLayout.SetColumnSpan(_chkEventLog, 2);
            row++;

            // 7. イベントログ周期
            Label lblEventLogInterval = new Label();
            lblEventLogInterval.Text = "　履歴・イベント記録周期 (秒):";
            lblEventLogInterval.AutoSize = true;
            lblEventLogInterval.Dock = DockStyle.Fill;
            lblEventLogInterval.TextAlign = ContentAlignment.MiddleLeft;
            lblEventLogInterval.Margin = new Padding(0, 0, (int)(15 * dpiScale), (int)(12 * dpiScale));

            _numEventLogInterval = new NumericUpDown();
            _numEventLogInterval.Minimum = 5;
            _numEventLogInterval.Maximum = 3600;
            _numEventLogInterval.Width = (int)(110 * dpiScale);
            _numEventLogInterval.Margin = new Padding(0, 0, 0, (int)(12 * dpiScale));

            mainLayout.Controls.Add(lblEventLogInterval, 0, row);
            mainLayout.Controls.Add(_numEventLogInterval, 1, row);
            row++;

            // 8. 文字色設定 (ドロップダウン)
            Label lblColor = new Label();
            lblColor.Text = "トレイアイコン文字色 (CPU):";
            lblColor.AutoSize = true;
            lblColor.Dock = DockStyle.Fill;
            lblColor.TextAlign = ContentAlignment.MiddleLeft;
            lblColor.Margin = new Padding(0, 0, (int)(15 * dpiScale), (int)(16 * dpiScale));

            _cmbTextColor = new ComboBox();
            _cmbTextColor.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmbTextColor.Items.AddRange(new object[] {
                "自動 (タスクバーに自動追従) [推奨]",
                "白色 (ダークモード用)",
                "黒色 (ライトモード用)"
            });
            _cmbTextColor.Width = (int)(290 * dpiScale);
            _cmbTextColor.Margin = new Padding(0, 0, 0, (int)(16 * dpiScale));

            mainLayout.Controls.Add(lblColor, 0, row);
            mainLayout.Controls.Add(_cmbTextColor, 1, row);
            row++;

            // 9. ボタン群 (右寄せ)
            FlowLayoutPanel buttonPanel = new FlowLayoutPanel();
            buttonPanel.Dock = DockStyle.Fill;
            buttonPanel.FlowDirection = FlowDirection.RightToLeft;
            buttonPanel.AutoSize = true;
            buttonPanel.Margin = new Padding(0, (int)(8 * dpiScale), 0, 0);

            int btnHeight = (int)Math.Round(this.Font.Height * 1.45f);
            if (btnHeight < (int)(26 * dpiScale)) btnHeight = (int)(26 * dpiScale);

            Button btnCancel = new Button();
            btnCancel.Text = "キャンセル";
            btnCancel.DialogResult = DialogResult.Cancel;
            btnCancel.AutoSize = true;
            btnCancel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            btnCancel.Padding = new Padding((int)(16 * dpiScale), 0, (int)(16 * dpiScale), 0);
            btnCancel.Height = btnHeight;
            btnCancel.Margin = new Padding((int)(8 * dpiScale), 0, 0, 0);

            Button btnOk = new Button();
            btnOk.Text = "OK";
            btnOk.DialogResult = DialogResult.OK;
            btnOk.AutoSize = true;
            btnOk.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            btnOk.Padding = new Padding((int)(20 * dpiScale), 0, (int)(20 * dpiScale), 0);
            btnOk.Height = btnHeight;
            btnOk.Click += OnSave;

            buttonPanel.Controls.Add(btnCancel);
            buttonPanel.Controls.Add(btnOk);

            mainLayout.Controls.Add(buttonPanel, 0, row);
            mainLayout.SetColumnSpan(buttonPanel, 2);

            this.Controls.Add(mainLayout);
            this.AcceptButton = btnOk;
            this.CancelButton = btnCancel;
        }

        private List<HardwareDeviceSummary> GetDefaultDeviceList()
        {
            List<HardwareDeviceSummary> list = new List<HardwareDeviceSummary>();
            list.Add(new HardwareDeviceSummary("CPU", DeviceCategory.Cpu, "CPU", "CPU 温度", null));
            list.Add(new HardwareDeviceSummary("iGPU", DeviceCategory.IGpu, "iGPU", "iGPU 温度 (内蔵グラフィックス)", null));
            list.Add(new HardwareDeviceSummary("dGPU:0", DeviceCategory.DGpu, "dGPU", "dGPU #0 温度 (専用グラフィックス)", null));
            return list;
        }

        private void LoadValues()
        {
            _numCpuThreshold.Value = ClampDecimal((decimal)_settings.WarningThresholdCelsius, _numCpuThreshold);
            _numIGpuThreshold.Value = ClampDecimal((decimal)_settings.WarningThresholdIGpuCelsius, _numIGpuThreshold);
            _numDGpuThreshold.Value = ClampDecimal((decimal)_settings.WarningThresholdDGpuCelsius, _numDGpuThreshold);
            _numInterval.Value = ClampDecimal(_settings.UpdateIntervalMs / 1000m, _numInterval);

            foreach (KeyValuePair<string, CheckBox> kv in _deviceCheckBoxes)
            {
                kv.Value.Checked = _settings.IsDeviceInTray(kv.Key);
            }

            _chkEventLog.Checked = _settings.EnableEventLogging;
            _numEventLogInterval.Value = ClampDecimal(_settings.EventLogIntervalSeconds, _numEventLogInterval);
            _numEventLogInterval.Enabled = _settings.EnableEventLogging;

            if (string.Equals(_settings.TextColorMode, "White", StringComparison.OrdinalIgnoreCase))
            {
                _cmbTextColor.SelectedIndex = 1;
            }
            else if (string.Equals(_settings.TextColorMode, "Black", StringComparison.OrdinalIgnoreCase))
            {
                _cmbTextColor.SelectedIndex = 2;
            }
            else
            {
                _cmbTextColor.SelectedIndex = 0; // 自動
            }
        }

        private static decimal ClampDecimal(decimal value, NumericUpDown control)
        {
            if (value < control.Minimum) return control.Minimum;
            if (value > control.Maximum) return control.Maximum;
            return value;
        }

        private void OnSave(object sender, EventArgs e)
        {
            bool hasAnyChecked = false;
            foreach (CheckBox chk in _deviceCheckBoxes.Values)
            {
                if (chk.Checked)
                {
                    hasAnyChecked = true;
                    break;
                }
            }

            if (!hasAnyChecked)
            {
                MessageBox.Show(this, "タスクトレイには少なくとも 1 つのデバイスを表示する必要があります。", "設定", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                this.DialogResult = DialogResult.None;
                return;
            }

            _settings.WarningThresholdCelsius = (float)_numCpuThreshold.Value;
            _settings.WarningThresholdIGpuCelsius = (float)_numIGpuThreshold.Value;
            _settings.WarningThresholdDGpuCelsius = (float)_numDGpuThreshold.Value;

            foreach (KeyValuePair<string, CheckBox> kv in _deviceCheckBoxes)
            {
                _settings.SetDeviceInTray(kv.Key, kv.Value.Checked);
            }

            _settings.UpdateIntervalMs = (int)(_numInterval.Value * 1000);
            _settings.EnableEventLogging = _chkEventLog.Checked;
            _settings.EventLogIntervalSeconds = (int)_numEventLogInterval.Value;

            switch (_cmbTextColor.SelectedIndex)
            {
                case 1:
                    _settings.TextColorMode = "White";
                    break;
                case 2:
                    _settings.TextColorMode = "Black";
                    break;
                default:
                    _settings.TextColorMode = "Auto";
                    break;
            }
        }
    }
}
