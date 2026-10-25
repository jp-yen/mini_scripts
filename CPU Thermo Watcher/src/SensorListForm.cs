// --------------------------------------------------------------------------------------------------------------------
// <copyright file="SensorListForm.cs" company="CPU Thermo Watcher Contributors">
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
    public class SensorListForm : Form
    {
        private DataGridView _grid;
        private Label _statusLabel;
        private float _threshold;

        public SensorListForm(CpuTemperatureSnapshot initialSnapshot, float threshold)
        {
            _threshold = threshold;
            InitializeComponent();
            if (initialSnapshot != null)
            {
                UpdateSnapshot(initialSnapshot, threshold);
            }
        }

        private void InitializeComponent()
        {
            this.Text = "CPU / iGPU / dGPU センサー温度一覧";
            this.Font = new Font("Segoe UI", 10f, FontStyle.Regular);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Icon = SystemIcons.Information;

            // DPI スケールの動的計算
            float dpiScale = 1.0f;
            using (Graphics g = this.CreateGraphics())
            {
                dpiScale = g.DpiY / 96.0f;
            }
            if (dpiScale < 1.0f) dpiScale = 1.0f;

            this.ClientSize = new Size((int)(720 * dpiScale), (int)(520 * dpiScale));
            this.MinimumSize = new Size((int)(560 * dpiScale), (int)(380 * dpiScale));

            // 上部パネル
            TableLayoutPanel topPanel = new TableLayoutPanel();
            topPanel.Dock = DockStyle.Top;
            topPanel.Height = (int)(52 * dpiScale);
            topPanel.ColumnCount = 2;
            topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            topPanel.Padding = new Padding((int)(12 * dpiScale), (int)(8 * dpiScale), (int)(12 * dpiScale), (int)(8 * dpiScale));

            _statusLabel = new Label();
            _statusLabel.Dock = DockStyle.Fill;
            _statusLabel.Text = "取得中...";
            _statusLabel.Font = new Font(this.Font, FontStyle.Bold);
            _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            _statusLabel.AutoEllipsis = false;

            CheckBox chkTopMost = new CheckBox();
            chkTopMost.Dock = DockStyle.Fill;
            chkTopMost.Text = "常に手前に表示";
            chkTopMost.AutoSize = true;
            chkTopMost.TextAlign = ContentAlignment.MiddleRight;
            chkTopMost.Margin = new Padding((int)(8 * dpiScale), 0, 0, 0);
            chkTopMost.CheckedChanged += delegate { this.TopMost = chkTopMost.Checked; };

            topPanel.Controls.Add(_statusLabel, 0, 0);
            topPanel.Controls.Add(chkTopMost, 1, 0);

            // DataGridView の設定
            _grid = new DataGridView();
            _grid.Dock = DockStyle.Fill;
            _grid.ReadOnly = true;
            _grid.AllowUserToAddRows = false;
            _grid.AllowUserToDeleteRows = false;
            _grid.AllowUserToResizeRows = false;
            _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _grid.MultiSelect = false;
            _grid.RowHeadersVisible = false;
            _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            _grid.BackgroundColor = Color.White;
            _grid.BorderStyle = BorderStyle.None;

            _grid.EnableHeadersVisualStyles = false;
            _grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(242, 244, 247);
            _grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(40, 40, 40);
            _grid.ColumnHeadersDefaultCellStyle.Font = new Font(this.Font, FontStyle.Bold);
            _grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
            int hPad = (int)Math.Round(8 * dpiScale);
            _grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(hPad, 2, hPad, 2);

            _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            _grid.ColumnHeadersHeight = (int)Math.Round(this.Font.Height * 1.5f);

            int rowHeight = (int)Math.Round(this.Font.Height * 1.25f);
            if (rowHeight < (int)(20 * dpiScale)) rowHeight = (int)(20 * dpiScale);
            _grid.RowTemplate.Height = rowHeight;
            _grid.DefaultCellStyle.Padding = new Padding(hPad, 0, hPad, 0);

            // 4列の定義: 区分, ハードウェア名, センサー名, 現在温度
            _grid.Columns.Add("Device", "区分");
            _grid.Columns.Add("Hardware", "ハードウェア名");
            _grid.Columns.Add("Sensor", "センサー名");
            _grid.Columns.Add("Value", "現在温度 (℃)");

            _grid.Columns["Device"].FillWeight = 16;
            _grid.Columns["Hardware"].FillWeight = 32;
            _grid.Columns["Sensor"].FillWeight = 32;
            _grid.Columns["Value"].FillWeight = 20;
            _grid.Columns["Value"].MinimumWidth = (int)(120 * dpiScale);
            _grid.Columns["Value"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            _grid.Columns["Value"].HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleRight;

            this.Controls.Add(_grid);
            this.Controls.Add(topPanel);
        }

        public void UpdateSnapshot(CpuTemperatureSnapshot snapshot, float threshold)
        {
            _threshold = threshold;

            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action<CpuTemperatureSnapshot, float>(UpdateSnapshot), snapshot, threshold);
                return;
            }

            if (snapshot == null || !snapshot.Success || snapshot.Sensors.Count == 0)
            {
                _statusLabel.Text = "温度センサーが取得できませんでした。";
                _grid.Rows.Clear();
                return;
            }

            // 1. ヘッダーテキストの構築
            List<string> summaries = new List<string>();
            if (snapshot.Devices != null && snapshot.Devices.Count > 0)
            {
                for (int i = 0; i < snapshot.Devices.Count; i++)
                {
                    HardwareDeviceSummary dev = snapshot.Devices[i];
                    if (dev.MaxSensor != null)
                    {
                        summaries.Add(string.Format("{0}: {1:F1}℃", dev.DeviceId.Replace(":", " #"), dev.MaxSensor.Value));
                    }
                }
            }
            else
            {
                if (snapshot.MaxCpuSensor != null) summaries.Add(string.Format("CPU: {0:F1}℃", snapshot.MaxCpuSensor.Value));
                if (snapshot.MaxGpuSensor != null) summaries.Add(string.Format("GPU: {0:F1}℃", snapshot.MaxGpuSensor.Value));
            }

            summaries.Add(string.Format("計 {0} センサー", snapshot.Sensors.Count));
            _statusLabel.Text = string.Join(" | ", summaries.ToArray());

            // 2. グリッド行の再構築（センサー数が変化した場合のみ）
            if (_grid.Rows.Count != snapshot.Sensors.Count)
            {
                int prevSelectedIndex = _grid.SelectedRows.Count > 0 ? _grid.SelectedRows[0].Index : -1;

                _grid.SuspendLayout();
                _grid.Rows.Clear();

                for (int i = 0; i < snapshot.Sensors.Count; i++)
                {
                    TemperatureReading r = snapshot.Sensors[i];
                    string devLabel = (r.DeviceId ?? "CPU").Replace(":", " #");
                    _grid.Rows.Add(devLabel, r.HardwareName, r.SensorName, string.Format("{0:F1} ℃", r.Value));
                }

                if (prevSelectedIndex >= 0 && prevSelectedIndex < _grid.Rows.Count)
                {
                    _grid.ClearSelection();
                    _grid.Rows[prevSelectedIndex].Selected = true;
                }

                _grid.ResumeLayout();
            }

            // 3. 各行の値およびハイライトの更新
            for (int i = 0; i < snapshot.Sensors.Count; i++)
            {
                TemperatureReading r = snapshot.Sensors[i];
                DataGridViewRow row = _grid.Rows[i];

                row.Cells["Value"].Value = string.Format("{0:F1} ℃", r.Value);
                if (row.Height != _grid.RowTemplate.Height)
                {
                    row.Height = _grid.RowTemplate.Height;
                }

                row.DefaultCellStyle.Font = _grid.Font;

                // 各デバイスの最高温度判定
                HardwareDeviceSummary dev = snapshot.GetDevice(r.DeviceId);
                bool isDeviceMax = (dev != null && dev.MaxSensor != null &&
                                    string.Equals(r.SensorName, dev.MaxSensor.SensorName, StringComparison.OrdinalIgnoreCase));

                if (isDeviceMax)
                {
                    if (r.Category == DeviceCategory.Cpu)
                    {
                        row.DefaultCellStyle.BackColor = Color.FromArgb(235, 245, 255); // ソフトブルー (CPU)
                    }
                    else if (r.Category == DeviceCategory.IGpu)
                    {
                        row.DefaultCellStyle.BackColor = Color.FromArgb(230, 255, 240); // ソフトグリーン (iGPU)
                    }
                    else
                    {
                        if (string.Equals(r.DeviceId, "dGPU:0", StringComparison.OrdinalIgnoreCase))
                        {
                            row.DefaultCellStyle.BackColor = Color.FromArgb(255, 245, 225); // ソフトオレンジ (dGPU #0)
                        }
                        else
                        {
                            row.DefaultCellStyle.BackColor = Color.FromArgb(255, 235, 240); // ソフトピンク (dGPU #1+)
                        }
                    }

                    if (r.Value >= _threshold)
                    {
                        row.DefaultCellStyle.ForeColor = Color.Crimson;
                    }
                    else
                    {
                        row.DefaultCellStyle.ForeColor = Color.Black;
                    }
                }
                else
                {
                    row.DefaultCellStyle.BackColor = Color.White;
                    row.DefaultCellStyle.ForeColor = (r.Value >= _threshold) ? Color.Crimson : Color.Black;
                }
            }
        }
    }
}
