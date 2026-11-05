// --------------------------------------------------------------------------------------------------------------------
// <copyright file="HistoryViewerForm.cs" company="CPU Thermo Watcher Contributors">
//   Copyright (c) 2026, CPU Thermo Watcher Contributors. All rights reserved.
//   Licensed under the 3-Clause BSD License.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace CpuThermoWatcher
{
    public class HistoryViewerForm : Form
    {
        private DataGridView _grid;
        private ComboBox _cmbRange;
        private DateTimePicker _dtpStart;
        private DateTimePicker _dtpEnd;
        private Label _lblTilde;
        private ComboBox _cmbDevice;
        private ComboBox _cmbSensor;
        private TextBox _txtKeyword;
        private CheckBox _chkOnlyMax;
        private ComboBox _cmbLimit;
        private Label _lblSummary;
        private float _dpiScale = 1.0f;

        public HistoryViewerForm()
        {
            InitializeComponent();
            EnableDoubleBuffering(_grid);
            LoadDeviceList();
            LoadSensorList();
            ApplyFilter();
        }

        private static void EnableDoubleBuffering(Control ctl)
        {
            try
            {
                PropertyInfo prop = typeof(Control).GetProperty("DoubleBuffered", BindingFlags.NonPublic | BindingFlags.Instance);
                if (prop != null)
                {
                    prop.SetValue(ctl, true, null);
                }
            }
            catch
            {
            }
        }

        private void InitializeComponent()
        {
            this.Text = "温度履歴データ ビューワー (SQLite データベース)";
            this.Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Icon = SystemIcons.Application;

            using (Graphics g = this.CreateGraphics())
            {
                _dpiScale = g.DpiY / 96.0f;
            }
            if (_dpiScale < 1.0f) _dpiScale = 1.0f;

            this.ClientSize = new Size((int)(1100 * _dpiScale), (int)(640 * _dpiScale));
            this.MinimumSize = new Size((int)(800 * _dpiScale), (int)(480 * _dpiScale));

            int ctlHeight = (int)Math.Round(this.Font.Height * 1.35f);
            if (ctlHeight < (int)(24 * _dpiScale)) ctlHeight = (int)(24 * _dpiScale);

            // 上部フィルタパネル (2段レイアウトで整頓)
            Panel topContainer = new Panel();
            topContainer.Dock = DockStyle.Top;
            topContainer.AutoSize = true;
            topContainer.Padding = new Padding((int)(12 * _dpiScale), (int)(8 * _dpiScale), (int)(12 * _dpiScale), (int)(8 * _dpiScale));
            topContainer.BackColor = Color.FromArgb(248, 249, 250);

            FlowLayoutPanel row1 = new FlowLayoutPanel();
            row1.Dock = DockStyle.Top;
            row1.AutoSize = true;
            row1.FlowDirection = FlowDirection.LeftToRight;
            row1.WrapContents = true;
            row1.Margin = new Padding(0);

            FlowLayoutPanel row2 = new FlowLayoutPanel();
            row2.Dock = DockStyle.Top;
            row2.AutoSize = true;
            row2.FlowDirection = FlowDirection.LeftToRight;
            row2.WrapContents = true;
            row2.Margin = new Padding(0, (int)(6 * _dpiScale), 0, 0);

            // --- Row 1: 期間指定 & アクション ---
            Label lblRange = new Label();
            lblRange.Text = "期間:";
            lblRange.AutoSize = true;
            lblRange.Margin = new Padding(0, (int)(4 * _dpiScale), (int)(4 * _dpiScale), 0);

            _cmbRange = new ComboBox();
            _cmbRange.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmbRange.Items.AddRange(new object[] { "直近 1 時間", "直近 6 時間", "直近 24 時間", "直近 7 日間", "全期間", "カスタム指定..." });
            _cmbRange.SelectedIndex = 2; // デフォルト直近24時間
            _cmbRange.Width = (int)(135 * _dpiScale);
            _cmbRange.Margin = new Padding(0, 0, (int)(8 * _dpiScale), 0);
            _cmbRange.SelectedIndexChanged += OnRangeIndexChanged;

            _dtpStart = new DateTimePicker();
            _dtpStart.Format = DateTimePickerFormat.Custom;
            _dtpStart.CustomFormat = "yyyy/MM/dd HH:mm";
            _dtpStart.Width = (int)(145 * _dpiScale);
            _dtpStart.Value = DateTime.Now.AddDays(-1);
            _dtpStart.Visible = false;
            _dtpStart.Margin = new Padding(0, 0, (int)(4 * _dpiScale), 0);

            _lblTilde = new Label();
            _lblTilde.Text = "～";
            _lblTilde.AutoSize = true;
            _lblTilde.Visible = false;
            _lblTilde.Margin = new Padding(0, (int)(4 * _dpiScale), (int)(4 * _dpiScale), 0);

            _dtpEnd = new DateTimePicker();
            _dtpEnd.Format = DateTimePickerFormat.Custom;
            _dtpEnd.CustomFormat = "yyyy/MM/dd HH:mm";
            _dtpEnd.Width = (int)(145 * _dpiScale);
            _dtpEnd.Value = DateTime.Now;
            _dtpEnd.Visible = false;
            _dtpEnd.Margin = new Padding(0, 0, (int)(10 * _dpiScale), 0);

            Label lblLimit = new Label();
            lblLimit.Text = "上限:";
            lblLimit.AutoSize = true;
            lblLimit.Margin = new Padding((int)(6 * _dpiScale), (int)(4 * _dpiScale), (int)(4 * _dpiScale), 0);

            _cmbLimit = new ComboBox();
            _cmbLimit.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmbLimit.Items.AddRange(new object[] { "1,000 件", "3,000 件", "10,000 件", "全件 (無制限)" });
            _cmbLimit.SelectedIndex = 0;
            _cmbLimit.Width = (int)(105 * _dpiScale);
            _cmbLimit.Margin = new Padding(0, 0, (int)(12 * _dpiScale), 0);

            Button btnSearch = new Button();
            btnSearch.Text = "絞り込み実行 (&F)";
            btnSearch.AutoSize = true;
            btnSearch.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            btnSearch.Padding = new Padding((int)(12 * _dpiScale), 0, (int)(12 * _dpiScale), 0);
            btnSearch.Height = ctlHeight;
            btnSearch.BackColor = Color.FromArgb(0, 120, 215);
            btnSearch.ForeColor = Color.White;
            btnSearch.FlatStyle = FlatStyle.System;
            btnSearch.Margin = new Padding(0, 0, (int)(8 * _dpiScale), 0);
            btnSearch.Click += delegate { ApplyFilter(); };

            Button btnExport = new Button();
            btnExport.Text = "CSV エクスポート (&E)...";
            btnExport.AutoSize = true;
            btnExport.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            btnExport.Padding = new Padding((int)(12 * _dpiScale), 0, (int)(12 * _dpiScale), 0);
            btnExport.Height = ctlHeight;
            btnExport.Margin = new Padding(0, 0, 0, 0);
            btnExport.Click += OnExportCsv;

            row1.Controls.Add(lblRange);
            row1.Controls.Add(_cmbRange);
            row1.Controls.Add(_dtpStart);
            row1.Controls.Add(_lblTilde);
            row1.Controls.Add(_dtpEnd);
            row1.Controls.Add(lblLimit);
            row1.Controls.Add(_cmbLimit);
            row1.Controls.Add(btnSearch);
            row1.Controls.Add(btnExport);

            // --- Row 2: デバイス・センサー・最高温度フィルタ ---
            Label lblDev = new Label();
            lblDev.Text = "デバイス:";
            lblDev.AutoSize = true;
            lblDev.Margin = new Padding(0, (int)(4 * _dpiScale), (int)(4 * _dpiScale), 0);

            _cmbDevice = new ComboBox();
            _cmbDevice.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmbDevice.Width = (int)(120 * _dpiScale);
            _cmbDevice.Margin = new Padding(0, 0, (int)(10 * _dpiScale), 0);
            _cmbDevice.SelectedIndexChanged += delegate
            {
                LoadSensorList();
            };

            Label lblSensor = new Label();
            lblSensor.Text = "センサー:";
            lblSensor.AutoSize = true;
            lblSensor.Margin = new Padding(0, (int)(4 * _dpiScale), (int)(4 * _dpiScale), 0);

            _cmbSensor = new ComboBox();
            _cmbSensor.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmbSensor.Width = (int)(200 * _dpiScale);
            _cmbSensor.Margin = new Padding(0, 0, (int)(10 * _dpiScale), 0);

            Label lblKw = new Label();
            lblKw.Text = "検索語:";
            lblKw.AutoSize = true;
            lblKw.Margin = new Padding(0, (int)(4 * _dpiScale), (int)(4 * _dpiScale), 0);

            _txtKeyword = new TextBox();
            _txtKeyword.Width = (int)(130 * _dpiScale);
            _txtKeyword.Margin = new Padding(0, 0, (int)(12 * _dpiScale), 0);
            _txtKeyword.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    ApplyFilter();
                }
            };

            _chkOnlyMax = new CheckBox();
            _chkOnlyMax.Text = "★ 各デバイス最高温度のみ (&M)";
            _chkOnlyMax.AutoSize = true;
            _chkOnlyMax.Checked = false;
            _chkOnlyMax.Font = new Font(this.Font, FontStyle.Bold);
            _chkOnlyMax.ForeColor = Color.FromArgb(0, 102, 204);
            _chkOnlyMax.Margin = new Padding(0, (int)(3 * _dpiScale), 0, 0);
            _chkOnlyMax.CheckedChanged += delegate { ApplyFilter(); };

            row2.Controls.Add(lblDev);
            row2.Controls.Add(_cmbDevice);
            row2.Controls.Add(lblSensor);
            row2.Controls.Add(_cmbSensor);
            row2.Controls.Add(lblKw);
            row2.Controls.Add(_txtKeyword);
            row2.Controls.Add(_chkOnlyMax);

            topContainer.Controls.Add(row2);
            topContainer.Controls.Add(row1);

            // 下部ステータスバー
            Panel bottomPanel = new Panel();
            bottomPanel.Dock = DockStyle.Bottom;
            bottomPanel.Height = (int)Math.Round(this.Font.Height * 1.8f + 8 * _dpiScale);
            bottomPanel.Padding = new Padding((int)(12 * _dpiScale), (int)(6 * _dpiScale), (int)(12 * _dpiScale), (int)(6 * _dpiScale));
            bottomPanel.BackColor = Color.FromArgb(245, 245, 245);

            _lblSummary = new Label();
            _lblSummary.Dock = DockStyle.Fill;
            _lblSummary.TextAlign = ContentAlignment.MiddleLeft;
            _lblSummary.Text = "待機中...";
            bottomPanel.Controls.Add(_lblSummary);

            // グリッドコントロール
            _grid = new DataGridView();
            _grid.Dock = DockStyle.Fill;
            _grid.ReadOnly = true;
            _grid.AllowUserToAddRows = false;
            _grid.AllowUserToDeleteRows = false;
            _grid.AllowUserToResizeRows = false;
            _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _grid.MultiSelect = true;
            _grid.BackgroundColor = Color.White;
            _grid.BorderStyle = BorderStyle.None;
            _grid.RowHeadersVisible = false;
            _grid.EnableHeadersVisualStyles = false;
            _grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(240, 242, 245);
            _grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(50, 50, 50);
            _grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9.0f, FontStyle.Bold);
            _grid.ColumnHeadersHeight = (int)(28 * _dpiScale);
            _grid.RowTemplate.Height = (int)(24 * _dpiScale);

            SetupGridColumns();

            this.Controls.Add(_grid);
            this.Controls.Add(bottomPanel);
            this.Controls.Add(topContainer);
        }

        private void SetupGridColumns()
        {
            _grid.Columns.Clear();
            _grid.AutoGenerateColumns = false;

            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Timestamp",
                HeaderText = "日時",
                Width = (int)(150 * _dpiScale),
                DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleCenter }
            });

            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "DeviceId",
                HeaderText = "デバイス",
                Width = (int)(85 * _dpiScale),
                DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleCenter }
            });

            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "SensorName",
                HeaderText = "センサー名",
                Width = (int)(240 * _dpiScale)
            });

            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Temperature",
                HeaderText = "温度 (℃)",
                Width = (int)(85 * _dpiScale),
                DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight, Format = "F1" }
            });

            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "DeviceName",
                HeaderText = "デバイス詳細 / 型番",
                Width = (int)(260 * _dpiScale)
            });

            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "IsDeviceMax",
                HeaderText = "最高温度フラグ",
                Width = (int)(120 * _dpiScale),
                DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleCenter }
            });

            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Status",
                HeaderText = "状態",
                Width = (int)(75 * _dpiScale),
                DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleCenter }
            });
        }

        private void OnRangeIndexChanged(object sender, EventArgs e)
        {
            bool isCustom = (_cmbRange.SelectedIndex == 5);
            _dtpStart.Visible = isCustom;
            _lblTilde.Visible = isCustom;
            _dtpEnd.Visible = isCustom;

            if (!isCustom)
            {
                ApplyFilter();
            }
        }

        private void LoadDeviceList()
        {
            List<string> devices = HistoryDatabase.GetDistinctDevices();
            _cmbDevice.Items.Clear();
            foreach (string d in devices)
            {
                _cmbDevice.Items.Add(d);
            }
            if (_cmbDevice.Items.Count > 0) _cmbDevice.SelectedIndex = 0;
        }

        private void LoadSensorList()
        {
            string selectedDev = _cmbDevice.SelectedItem != null ? _cmbDevice.SelectedItem.ToString() : "すべて";
            List<string> sensors = HistoryDatabase.GetDistinctSensors(selectedDev);
            _cmbSensor.Items.Clear();
            foreach (string s in sensors)
            {
                _cmbSensor.Items.Add(s);
            }
            if (_cmbSensor.Items.Count > 0) _cmbSensor.SelectedIndex = 0;
        }

        private void GetCurrentFilter(
            out DateTime? startTime,
            out DateTime? endTime,
            out string deviceId,
            out string sensorFilter,
            out bool onlyDeviceMax,
            out int limit)
        {
            startTime = null;
            endTime = null;
            DateTime now = DateTime.Now;

            switch (_cmbRange.SelectedIndex)
            {
                case 0: startTime = now.AddHours(-1); break;
                case 1: startTime = now.AddHours(-6); break;
                case 2: startTime = now.AddHours(-24); break;
                case 3: startTime = now.AddDays(-7); break;
                case 4: startTime = null; break; // 全期間
                case 5:
                    startTime = _dtpStart.Value;
                    endTime = _dtpEnd.Value;
                    break;
            }

            deviceId = _cmbDevice.SelectedItem != null ? _cmbDevice.SelectedItem.ToString() : "すべて";

            string selectedSensor = _cmbSensor.SelectedItem != null ? _cmbSensor.SelectedItem.ToString() : "すべて";
            onlyDeviceMax = _chkOnlyMax.Checked;

            sensorFilter = string.Empty;
            if (!selectedSensor.Equals("すべて", StringComparison.OrdinalIgnoreCase))
            {
                sensorFilter = selectedSensor;
            }

            string kw = _txtKeyword.Text.Trim();
            if (!string.IsNullOrEmpty(kw))
            {
                sensorFilter = kw;
            }

            limit = 1000;
            switch (_cmbLimit.SelectedIndex)
            {
                case 0: limit = 1000; break;
                case 1: limit = 3000; break;
                case 2: limit = 10000; break;
                case 3: limit = 0; break; // 無制限
            }
        }

        public void OnLogWritten()
        {
            if (this.IsDisposed) return;
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            DateTime? startTime;
            DateTime? endTime;
            string deviceId;
            string sensorFilter;
            bool onlyDeviceMax;
            int limit;
            GetCurrentFilter(out startTime, out endTime, out deviceId, out sensorFilter, out onlyDeviceMax, out limit);

            List<HistoryRecord> records = HistoryDatabase.QueryRecords(startTime, endTime, deviceId, sensorFilter, onlyDeviceMax, limit);

            _grid.Rows.Clear();

            if (records.Count == 0)
            {
                _lblSummary.Text = "条件に一致する履歴レコードは見つかりませんでした。";
                return;
            }

            float minTemp = float.MaxValue;
            float maxTemp = float.MinValue;
            float sumTemp = 0f;

            _grid.SuspendLayout();
            for (int i = 0; i < records.Count; i++)
            {
                HistoryRecord r = records[i];
                if (r.Temperature < minTemp) minTemp = r.Temperature;
                if (r.Temperature > maxTemp) maxTemp = r.Temperature;
                sumTemp += r.Temperature;

                int rowIdx = _grid.Rows.Add(
                    r.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"),
                    r.DeviceId,
                    r.SensorName,
                    r.Temperature,
                    r.DeviceName,
                    r.IsDeviceMax ? "★ 最高温度" : "",
                    r.IsWarning ? "警告" : "正常"
                );

                DataGridViewRow row = _grid.Rows[rowIdx];
                if (r.IsWarning)
                {
                    row.DefaultCellStyle.BackColor = Color.FromArgb(255, 235, 235);
                    row.DefaultCellStyle.ForeColor = Color.DarkRed;
                }
                else if (r.IsDeviceMax)
                {
                    row.DefaultCellStyle.BackColor = Color.FromArgb(240, 248, 255);
                    row.Cells["SensorName"].Style.Font = new Font(_grid.Font, FontStyle.Bold);
                }
            }
            _grid.ResumeLayout();

            float avgTemp = sumTemp / records.Count;
            _lblSummary.Text = string.Format("表示件数: {0:N0} 件 | 最高: {1:F1}℃ / 最低: {2:F1}℃ / 平均: {3:F1}℃",
                records.Count, maxTemp, minTemp, avgTemp);
        }

        private void OnExportCsv(object sender, EventArgs e)
        {
            DateTime? startTime;
            DateTime? endTime;
            string deviceId;
            string sensorFilter;
            bool onlyDeviceMax;
            int limit;
            GetCurrentFilter(out startTime, out endTime, out deviceId, out sensorFilter, out onlyDeviceMax, out limit);

            using (SaveFileDialog dlg = new SaveFileDialog())
            {
                dlg.Title = "温度履歴データを CSV エクスポート";
                dlg.Filter = "CSV ファイル (*.csv)|*.csv|すべてのファイル (*.*)|*.*";
                dlg.FileName = string.Format("CPU_Thermo_History_{0:yyyyMMdd_HHmmss}.csv", DateTime.Now);

                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    try
                    {
                        int exportedCount = HistoryDatabase.ExportCsv(dlg.FileName, startTime, endTime, deviceId, sensorFilter, onlyDeviceMax);
                        MessageBox.Show(this,
                            string.Format("CSV エクスポートが完了しました。\n\n出力件数: {0:N0} 件\n保存先: {1}", exportedCount, dlg.FileName),
                            "エクスポート完了",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(this,
                            "CSV の保存中にエラーが発生しました:\n" + ex.Message,
                            "エラー",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                    }
                }
            }
        }
    }
}
