// --------------------------------------------------------------------------------------------------------------------
// <copyright file="GraphViewerForm.cs" company="CPU Thermo Watcher Contributors">
//   Copyright (c) 2026, CPU Thermo Watcher Contributors. All rights reserved.
//   Licensed under the 3-Clause BSD License.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;

namespace CpuThermoWatcher
{
    public class GraphViewerForm : Form
    {
        private Chart _chart;
        private ComboBox _cmbRange;
        private DateTimePicker _dtpStart;
        private DateTimePicker _dtpEnd;
        private Label _lblTilde;
        private Button _btnApplyCustom;
        private CheckBox _chkAutoRefresh;
        private Label _lblSummary;
        private StripLine _thresholdStripLine;
        private float _threshold;
        private float _dpiScale = 1.0f;
        private ToolTip _chartToolTip;
        private string _lastHitSeries = null;
        private int _lastHitPointIndex = -1;

        public GraphViewerForm(float threshold)
        {
            _threshold = threshold;
            InitializeComponent();
            LoadData();
        }

        private void InitializeComponent()
        {
            this.Text = "CPU / iGPU / dGPU 温度推移グラフ (SQLite 履歴)";
            this.Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Icon = SystemIcons.Application;

            // DPI スケールの動的計算
            using (Graphics g = this.CreateGraphics())
            {
                _dpiScale = g.DpiY / 96.0f;
            }
            if (_dpiScale < 1.0f) _dpiScale = 1.0f;

            this.ClientSize = new Size((int)(1000 * _dpiScale), (int)(620 * _dpiScale));
            this.MinimumSize = new Size((int)(720 * _dpiScale), (int)(460 * _dpiScale));

            int ctlHeight = (int)Math.Round(this.Font.Height * 1.35f);
            if (ctlHeight < (int)(24 * _dpiScale)) ctlHeight = (int)(24 * _dpiScale);

            // 上部ツールバー
            FlowLayoutPanel toolPanel = new FlowLayoutPanel();
            toolPanel.Dock = DockStyle.Top;
            toolPanel.AutoSize = true;
            toolPanel.FlowDirection = FlowDirection.LeftToRight;
            toolPanel.WrapContents = true;
            toolPanel.Padding = new Padding((int)(14 * _dpiScale), (int)(8 * _dpiScale), (int)(14 * _dpiScale), (int)(8 * _dpiScale));

            Label lblRange = new Label();
            lblRange.Text = "表示期間:";
            lblRange.AutoSize = true;
            lblRange.Margin = new Padding(0, (int)(4 * _dpiScale), (int)(8 * _dpiScale), 0);

            _cmbRange = new ComboBox();
            _cmbRange.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmbRange.Items.AddRange(new object[] { "直近 1 時間", "直近 6 時間", "直近 24 時間", "直近 7 日間", "全期間", "カスタム指定..." });
            _cmbRange.SelectedIndex = 0; // 直近1時間
            _cmbRange.Width = (int)(150 * _dpiScale);
            _cmbRange.Margin = new Padding(0, (int)(1 * _dpiScale), (int)(10 * _dpiScale), 0);
            _cmbRange.SelectedIndexChanged += OnRangeChanged;

            // カスタム日時指定コントロール群
            _dtpStart = new DateTimePicker();
            _dtpStart.Format = DateTimePickerFormat.Custom;
            _dtpStart.CustomFormat = "yyyy/MM/dd HH:mm";
            _dtpStart.Width = (int)(150 * _dpiScale);
            _dtpStart.Value = DateTime.Now.AddHours(-1);
            _dtpStart.Visible = false;
            _dtpStart.Margin = new Padding(0, (int)(1 * _dpiScale), (int)(4 * _dpiScale), 0);

            _lblTilde = new Label();
            _lblTilde.Text = "～";
            _lblTilde.AutoSize = true;
            _lblTilde.Visible = false;
            _lblTilde.Margin = new Padding(0, (int)(4 * _dpiScale), (int)(4 * _dpiScale), 0);

            _dtpEnd = new DateTimePicker();
            _dtpEnd.Format = DateTimePickerFormat.Custom;
            _dtpEnd.CustomFormat = "yyyy/MM/dd HH:mm";
            _dtpEnd.Width = (int)(150 * _dpiScale);
            _dtpEnd.Value = DateTime.Now;
            _dtpEnd.Visible = false;
            _dtpEnd.Margin = new Padding(0, (int)(1 * _dpiScale), (int)(6 * _dpiScale), 0);

            _btnApplyCustom = new Button();
            _btnApplyCustom.Text = "適用 (&A)";
            _btnApplyCustom.AutoSize = true;
            _btnApplyCustom.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            _btnApplyCustom.Padding = new Padding((int)(10 * _dpiScale), 0, (int)(10 * _dpiScale), 0);
            _btnApplyCustom.Height = ctlHeight;
            _btnApplyCustom.Visible = false;
            _btnApplyCustom.Margin = new Padding(0, 0, (int)(12 * _dpiScale), 0);
            _btnApplyCustom.Click += delegate { LoadData(); };

            Button btnRefresh = new Button();
            btnRefresh.Text = "再読み込み (&R)";
            btnRefresh.AutoSize = true;
            btnRefresh.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            btnRefresh.Padding = new Padding((int)(12 * _dpiScale), 0, (int)(12 * _dpiScale), 0);
            btnRefresh.Height = ctlHeight;
            btnRefresh.Margin = new Padding(0, 0, (int)(12 * _dpiScale), 0);
            btnRefresh.Click += delegate { LoadData(); };

            _chkAutoRefresh = new CheckBox();
            _chkAutoRefresh.Text = "自動更新 (&U)";
            _chkAutoRefresh.Checked = true;
            _chkAutoRefresh.AutoSize = true;
            _chkAutoRefresh.Margin = new Padding(0, (int)(4 * _dpiScale), (int)(14 * _dpiScale), 0);
            _chkAutoRefresh.CheckedChanged += delegate
            {
                if (_chkAutoRefresh.Checked)
                {
                    LoadData();
                }
            };

            Button btnOpenEventVwr = new Button();
            btnOpenEventVwr.Text = "イベントビューアー";
            btnOpenEventVwr.AutoSize = true;
            btnOpenEventVwr.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            btnOpenEventVwr.Padding = new Padding((int)(10 * _dpiScale), 0, (int)(10 * _dpiScale), 0);
            btnOpenEventVwr.Height = ctlHeight;
            btnOpenEventVwr.Margin = new Padding(0, 0, 0, 0);
            btnOpenEventVwr.Click += delegate
            {
                try { Process.Start("eventvwr.msc"); } catch { }
            };

            toolPanel.Controls.Add(lblRange);
            toolPanel.Controls.Add(_cmbRange);
            toolPanel.Controls.Add(_dtpStart);
            toolPanel.Controls.Add(_lblTilde);
            toolPanel.Controls.Add(_dtpEnd);
            toolPanel.Controls.Add(_btnApplyCustom);
            toolPanel.Controls.Add(btnRefresh);
            toolPanel.Controls.Add(_chkAutoRefresh);
            toolPanel.Controls.Add(btnOpenEventVwr);

            // 下部ステータスバー
            Panel bottomPanel = new Panel();
            bottomPanel.Dock = DockStyle.Bottom;
            bottomPanel.Height = (int)Math.Round(this.Font.Height * 1.8f + 12 * _dpiScale);
            bottomPanel.Padding = new Padding((int)(14 * _dpiScale), (int)(6 * _dpiScale), (int)(14 * _dpiScale), (int)(6 * _dpiScale));
            bottomPanel.BackColor = Color.FromArgb(245, 245, 245);

            _lblSummary = new Label();
            _lblSummary.Dock = DockStyle.Fill;
            _lblSummary.TextAlign = ContentAlignment.MiddleLeft;
            _lblSummary.Text = "読み込み中...";
            bottomPanel.Controls.Add(_lblSummary);

            // チャートコントロール
            _chart = new Chart();
            _chart.Dock = DockStyle.Fill;
            _chart.BackColor = Color.White;

            ChartArea area = new ChartArea("Default");
            area.BackColor = Color.FromArgb(250, 252, 255);
            area.BorderColor = Color.Silver;
            area.BorderDashStyle = ChartDashStyle.Solid;

            // X軸（時刻）
            area.AxisX.LabelStyle.Font = new Font("Segoe UI", 8.5f);
            area.AxisX.LabelStyle.ForeColor = Color.FromArgb(60, 60, 60);
            area.AxisX.MajorGrid.LineColor = Color.FromArgb(230, 235, 242);
            area.AxisX.MajorGrid.LineDashStyle = ChartDashStyle.Dot;
            area.AxisX.LineColor = Color.Silver;

            // Y軸（温度 ℃）
            area.AxisY.Title = "温度 (℃)";
            area.AxisY.TitleFont = new Font("Segoe UI", 9.0f, FontStyle.Bold);
            area.AxisY.LabelStyle.Font = new Font("Segoe UI", 8.5f);
            area.AxisY.LabelStyle.ForeColor = Color.FromArgb(60, 60, 60);
            area.AxisY.MajorGrid.Interval = 10;
            area.AxisY.MajorGrid.LineColor = Color.Silver; // 枠線と同じ色 (Silver / グレー75%)
            area.AxisY.MajorGrid.LineDashStyle = ChartDashStyle.Solid;
            area.AxisY.MajorGrid.LineWidth = 1;
            area.AxisY.MinorGrid.Enabled = true;
            area.AxisY.MinorGrid.Interval = 5;
            area.AxisY.MinorGrid.LineColor = Color.FromArgb(230, 235, 242);
            area.AxisY.MinorGrid.LineDashStyle = ChartDashStyle.Dot;
            area.AxisY.LineColor = Color.Silver;
            area.AxisY.Interval = 5;

            // 警告温度しきい値ライン (赤破線)
            _thresholdStripLine = new StripLine();
            _thresholdStripLine.Interval = 0;
            _thresholdStripLine.IntervalOffset = _threshold;
            _thresholdStripLine.StripWidth = 0.0001;
            _thresholdStripLine.BorderColor = Color.FromArgb(220, 20, 20);
            _thresholdStripLine.BorderDashStyle = ChartDashStyle.Dash;
            _thresholdStripLine.BorderWidth = 1;
            _thresholdStripLine.Text = string.Format("CPU 警告閾値 ({0:F0}℃)", _threshold);
            _thresholdStripLine.TextAlignment = StringAlignment.Far;
            _thresholdStripLine.TextLineAlignment = StringAlignment.Near;
            _thresholdStripLine.ForeColor = Color.FromArgb(200, 20, 20);
            _thresholdStripLine.Font = new Font("Segoe UI", 8f, FontStyle.Bold);
            area.AxisY.StripLines.Add(_thresholdStripLine);

            _chart.ChartAreas.Add(area);

            // 凡例
            Legend legend = new Legend("Default");
            legend.Docking = Docking.Top;
            legend.Alignment = StringAlignment.Far;
            legend.Font = new Font("Segoe UI", 9.0f);
            legend.BackColor = Color.Transparent;
            _chart.Legends.Add(legend);

            // 基本系列の初期登録
            CreateSeries("CPU最高温度", Color.DodgerBlue, "CPU 最高温度");
            CreateSeries("iGPU最高温度", Color.MediumSeaGreen, "iGPU 最高温度");
            CreateSeries("dGPU #0最高温度", Color.DarkOrange, "dGPU #0 最高温度");

            // インタラクティブ Tooltip 設定
            _chartToolTip = new ToolTip();
            _chartToolTip.InitialDelay = 50;
            _chartToolTip.ReshowDelay = 50;
            _chartToolTip.AutoPopDelay = 10000;

            _chart.MouseMove += OnChartMouseMove;
            _chart.MouseLeave += delegate
            {
                if (_chartToolTip != null) _chartToolTip.Hide(_chart);
                _lastHitSeries = null;
                _lastHitPointIndex = -1;
            };

            this.Controls.Add(_chart);
            this.Controls.Add(bottomPanel);
            this.Controls.Add(toolPanel);
        }

        private void OnRangeChanged(object sender, EventArgs e)
        {
            bool isCustom = (_cmbRange.SelectedIndex == 5);
            _dtpStart.Visible = isCustom;
            _lblTilde.Visible = isCustom;
            _dtpEnd.Visible = isCustom;
            _btnApplyCustom.Visible = isCustom;

            if (!isCustom)
            {
                LoadData();
            }
        }

        private Series CreateSeries(string name, Color color, string legendText)
        {
            if (_chart.Series.IndexOf(name) >= 0)
            {
                return _chart.Series[name];
            }

            Series s = new Series(name);
            s.ChartType = SeriesChartType.Line;
            s.Color = color;
            s.BorderWidth = 2;
            s.MarkerStyle = MarkerStyle.Circle;
            s.MarkerSize = 4;
            s.MarkerColor = color;
            s.Legend = "Default";
            s.LegendText = legendText;
            s.XValueType = ChartValueType.DateTime;
            _chart.Series.Add(s);
            return s;
        }

        private void OnChartMouseMove(object sender, MouseEventArgs e)
        {
            string hitSeriesName;
            int pointIndex;
            float hitTolerance = 12f * _dpiScale;

            if (!FindNearestPoint(e.Location, hitTolerance, out hitSeriesName, out pointIndex))
            {
                if (_lastHitPointIndex != -1 || _lastHitSeries != null)
                {
                    _lastHitSeries = null;
                    _lastHitPointIndex = -1;
                    _chartToolTip.Hide(_chart);
                    _chart.Cursor = Cursors.Default;
                }
                return;
            }

            if (hitSeriesName == _lastHitSeries && pointIndex == _lastHitPointIndex)
            {
                return;
            }

            _lastHitSeries = hitSeriesName;
            _lastHitPointIndex = pointIndex;
            _chart.Cursor = Cursors.Hand;

            Series s = _chart.Series[hitSeriesName];
            DataPoint pt = s.Points[pointIndex];
            DateTime time = DateTime.FromOADate(pt.XValue);
            double temp = pt.YValues[0];
            string sensor = pt.Tag != null ? pt.Tag.ToString() : string.Empty;

            string seriesLabel = hitSeriesName.Replace("最高温度", "");
            string text = string.Format("[{0}] {1:yyyy/MM/dd HH:mm:ss}\n温度: {2:F1}℃", seriesLabel, time, temp);
            if (!string.IsNullOrEmpty(sensor))
            {
                text += string.Format("\nセンサー: {0}", sensor);
            }

            int tipX = e.X + (int)(14 * _dpiScale);
            int tipY = e.Y + (int)(14 * _dpiScale);
            if (tipX + (int)(180 * _dpiScale) > _chart.Width)
            {
                tipX = Math.Max(0, e.X - (int)(180 * _dpiScale));
            }
            if (tipY + (int)(70 * _dpiScale) > _chart.Height)
            {
                tipY = Math.Max(0, e.Y - (int)(70 * _dpiScale));
            }

            if (_chartToolTip != null)
            {
                _chartToolTip.Show(text, _chart, tipX, tipY);
            }
        }

        private bool FindNearestPoint(Point mouseLocation, float maxPixelDistance, out string outSeriesName, out int outPointIndex)
        {
            outSeriesName = null;
            outPointIndex = -1;
            if (_chart == null || _chart.Series.Count == 0 || _chart.ChartAreas.Count == 0) return false;
            ChartArea area = _chart.ChartAreas["Default"];
            if (area == null) return false;

            HitTestResult hit = _chart.HitTest(mouseLocation.X, mouseLocation.Y, ChartElementType.DataPoint);
            if (hit.ChartElementType == ChartElementType.DataPoint && hit.Series != null && hit.PointIndex >= 0)
            {
                outSeriesName = hit.Series.Name;
                outPointIndex = hit.PointIndex;
                return true;
            }

            try
            {
                double xVal = area.AxisX.PixelPositionToValue(mouseLocation.X);
                double bestDistSq = maxPixelDistance * maxPixelDistance;

                foreach (Series s in _chart.Series)
                {
                    if (s.Points.Count == 0) continue;
                    int low = 0;
                    int high = s.Points.Count - 1;
                    while (low <= high)
                    {
                        int mid = (low + high) / 2;
                        if (s.Points[mid].XValue < xVal) low = mid + 1;
                        else high = mid - 1;
                    }

                    int start = Math.Max(0, low - 3);
                    int end = Math.Min(s.Points.Count - 1, low + 3);

                    for (int i = start; i <= end; i++)
                    {
                        DataPoint pt = s.Points[i];
                        double px = area.AxisX.ValueToPixelPosition(pt.XValue);
                        double py = area.AxisY.ValueToPixelPosition(pt.YValues[0]);
                        double dx = px - mouseLocation.X;
                        double dy = py - mouseLocation.Y;
                        double distSq = dx * dx + dy * dy;
                        if (distSq < bestDistSq)
                        {
                            bestDistSq = distSq;
                            outSeriesName = s.Name;
                            outPointIndex = i;
                        }
                    }
                }

                return outPointIndex >= 0;
            }
            catch
            {
                return false;
            }
        }

        public void OnLogWritten(float threshold)
        {
            if (this.IsDisposed) return;

            if (Math.Abs(_threshold - threshold) > 0.01f)
            {
                _threshold = threshold;
                if (_thresholdStripLine != null)
                {
                    _thresholdStripLine.IntervalOffset = _threshold;
                    _thresholdStripLine.Text = string.Format("CPU 警告閾値 ({0:F0}℃)", _threshold);
                }
            }

            if (_chkAutoRefresh != null && _chkAutoRefresh.Checked)
            {
                LoadData();
            }
        }

        private void LoadData()
        {
            _lastHitSeries = null;
            _lastHitPointIndex = -1;
            if (_chartToolTip != null)
            {
                _chartToolTip.Hide(_chart);
            }
            if (_chart != null)
            {
                _chart.Cursor = Cursors.Default;
            }

            DateTime? startTime = null;
            DateTime? endTime = null;
            DateTime now = DateTime.Now;

            switch (_cmbRange.SelectedIndex)
            {
                case 0: startTime = now.AddHours(-1); break;
                case 1: startTime = now.AddHours(-6); break;
                case 2: startTime = now.AddHours(-24); break;
                case 3: startTime = now.AddDays(-7); break;
                case 4: startTime = null; break; // 全期間
                case 5: // カスタム期間指定
                    startTime = _dtpStart.Value;
                    endTime = _dtpEnd.Value;
                    break;
            }

            // SQLite から高速読み込み
            List<TemperatureLogEntry> logs = HistoryDatabase.ReadLogs(startTime, endTime);

            // フォールバック: SQLite にまだデータが無ければイベントログから読み込み
            if (logs.Count == 0 && _cmbRange.SelectedIndex != 5)
            {
                TimeSpan? tsRange = null;
                if (startTime.HasValue) tsRange = now - startTime.Value;
                logs = EventLogger.ReadLogs(tsRange);
            }

            foreach (Series s in _chart.Series)
            {
                s.Points.Clear();
            }

            if (logs.Count == 0)
            {
                _lblSummary.Text = "該当する期間の温度履歴記録が見つかりませんでした。";
                return;
            }

            // データ点数に応じたマーカーサイズ調整
            int markerSize = logs.Count > 200
                ? (int)Math.Max(2, Math.Round(1.2f * _dpiScale))
                : (int)Math.Max(3, Math.Round(2.0f * _dpiScale));

            Series cpuSeries = CreateSeries("CPU最高温度", Color.DodgerBlue, "CPU 最高温度");
            Series igpuSeries = CreateSeries("iGPU最高温度", Color.MediumSeaGreen, "iGPU 最高温度");

            cpuSeries.MarkerSize = markerSize;
            igpuSeries.MarkerSize = markerSize;

            float globalMin = float.MaxValue;
            float globalMax = float.MinValue;

            // 統計用アキュムレータ
            float cpuMin = float.MaxValue, cpuMax = float.MinValue, cpuSum = 0f;
            int cpuCount = 0;

            float igpuMin = float.MaxValue, igpuMax = float.MinValue, igpuSum = 0f;
            int igpuCount = 0;

            Dictionary<string, float> dgpuMin = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, float> dgpuMax = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, float> dgpuSum = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, int> dgpuCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            Color[] dgpuColors = new Color[] {
                Color.DarkOrange,       // dGPU:0
                Color.Crimson,          // dGPU:1
                Color.Gold,             // dGPU:2
                Color.MediumPurple      // dGPU:3
            };

            foreach (TemperatureLogEntry entry in logs)
            {
                // 1. CPU
                float cv = entry.MaxTemperature;
                if (cv > 0.0f)
                {
                    if (cv < cpuMin) cpuMin = cv;
                    if (cv > cpuMax) cpuMax = cv;
                    if (cv < globalMin) globalMin = cv;
                    if (cv > globalMax) globalMax = cv;
                    cpuSum += cv;
                    cpuCount++;

                    DataPoint cpuPt = new DataPoint();
                    cpuPt.SetValueXY(entry.Timestamp, cv);
                    cpuPt.Tag = entry.MaxSensorName;
                    cpuSeries.Points.Add(cpuPt);
                }

                // 2. iGPU
                if (entry.IGpuTemperature.HasValue)
                {
                    float igv = entry.IGpuTemperature.Value;
                    if (igv < igpuMin) igpuMin = igv;
                    if (igv > igpuMax) igpuMax = igv;
                    if (igv < globalMin) globalMin = igv;
                    if (igv > globalMax) globalMax = igv;
                    igpuSum += igv;
                    igpuCount++;

                    DataPoint igpuPt = new DataPoint();
                    igpuPt.SetValueXY(entry.Timestamp, igv);
                    igpuPt.Tag = entry.IGpuSensorName;
                    igpuSeries.Points.Add(igpuPt);
                }

                // 3. 各 dGPU
                int dgpuColorIdx = 0;
                foreach (KeyValuePair<string, float> kv in entry.DGpuTemperatures)
                {
                    string dgpuKey = kv.Key;
                    float dgv = kv.Value;

                    if (!dgpuMin.ContainsKey(dgpuKey))
                    {
                        dgpuMin[dgpuKey] = float.MaxValue;
                        dgpuMax[dgpuKey] = float.MinValue;
                        dgpuSum[dgpuKey] = 0f;
                        dgpuCount[dgpuKey] = 0;
                    }

                    if (dgv < dgpuMin[dgpuKey]) dgpuMin[dgpuKey] = dgv;
                    if (dgv > dgpuMax[dgpuKey]) dgpuMax[dgpuKey] = dgv;
                    if (dgv < globalMin) globalMin = dgv;
                    if (dgv > globalMax) globalMax = dgv;
                    dgpuSum[dgpuKey] += dgv;
                    dgpuCount[dgpuKey]++;

                    string seriesName = dgpuKey.Replace(":", " #") + "最高温度";
                    Color color = dgpuColors[dgpuColorIdx % dgpuColors.Length];
                    Series dgpuSeries = CreateSeries(seriesName, color, dgpuKey.Replace(":", " #") + " 最高温度");
                    dgpuSeries.MarkerSize = markerSize;

                    DataPoint dgpuPt = new DataPoint();
                    dgpuPt.SetValueXY(entry.Timestamp, dgv);
                    string sensorName = entry.DGpuSensorNames.ContainsKey(dgpuKey) ? entry.DGpuSensorNames[dgpuKey] : "GPU";
                    dgpuPt.Tag = sensorName;
                    dgpuSeries.Points.Add(dgpuPt);

                    dgpuColorIdx++;
                }
            }

            // 統計サマリー文字列の構築
            List<string> summaries = new List<string>();
            if (cpuCount > 0)
            {
                summaries.Add(string.Format("CPU [最高: {0:F1}℃ / 最低: {1:F1}℃ / 平均: {2:F1}℃]", cpuMax, cpuMin, cpuSum / cpuCount));
            }
            if (igpuCount > 0)
            {
                summaries.Add(string.Format("iGPU [最高: {0:F1}℃ / 最低: {1:F1}℃ / 平均: {2:F1}℃]", igpuMax, igpuMin, igpuSum / igpuCount));
            }
            foreach (string dgpuKey in dgpuCount.Keys)
            {
                int count = dgpuCount[dgpuKey];
                if (count > 0)
                {
                    summaries.Add(string.Format("{0} [最高: {1:F1}℃ / 最低: {2:F1}℃ / 平均: {3:F1}℃]",
                        dgpuKey.Replace(":", " #"), dgpuMax[dgpuKey], dgpuMin[dgpuKey], dgpuSum[dgpuKey] / count));
                }
            }

            _lblSummary.Text = string.Format("データ: {0} 件 | {1}", logs.Count, string.Join(" | ", summaries.ToArray()));

            if (globalMin == float.MaxValue) globalMin = 40f;
            if (globalMax == float.MinValue) globalMax = 80f;

            // Y軸の上限・下限を設定: 5*int((max+8)/5), 5*int((min-3)/5)
            double yMax = 5 * (int)((globalMax + 8f) / 5f);
            double yMin = 5 * (int)((globalMin - 3f) / 5f);
            if (yMin < 0) yMin = 0;
            if (yMax <= yMin) yMax = yMin + 5;

            ChartArea area = _chart.ChartAreas["Default"];
            area.AxisY.Minimum = yMin;
            area.AxisY.Maximum = yMax;
            area.AxisY.MajorGrid.Interval = 10;
            area.AxisY.MajorGrid.IntervalOffset = (10.0 - (yMin % 10.0)) % 10.0;

            // X軸フォーマット調整 (24時間以上なら日付付き)
            double spanHours = 1.0;
            if (logs.Count >= 2)
            {
                spanHours = (logs[logs.Count - 1].Timestamp - logs[0].Timestamp).TotalHours;
            }
            else if (_cmbRange.SelectedIndex >= 2)
            {
                spanHours = 25.0;
            }

            if (spanHours >= 24.0)
            {
                area.AxisX.LabelStyle.Format = "MM/dd HH:mm";
            }
            else
            {
                area.AxisX.LabelStyle.Format = "HH:mm";
            }

            _chart.Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_chartToolTip != null)
                {
                    _chartToolTip.Dispose();
                    _chartToolTip = null;
                }
            }
            base.Dispose(disposing);
        }
    }
}
