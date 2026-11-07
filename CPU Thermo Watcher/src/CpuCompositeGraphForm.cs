// --------------------------------------------------------------------------------------------------------------------
// <copyright file="CpuCompositeGraphForm.cs" company="CPU Thermo Watcher Contributors">
//   Copyright (c) 2026, CPU Thermo Watcher Contributors. All rights reserved.
//   Licensed under the 3-Clause BSD License.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;

namespace CpuThermoWatcher
{
    public class CpuCompositeGraphForm : Form
    {
        private Chart _chart;
        private ComboBox _cmbRange;
        private DateTimePicker _dtpStart;
        private DateTimePicker _dtpEnd;
        private Label _lblTilde;
        private Button _btnApplyCustom;
        private CheckBox _chkAutoRefresh;
        private Label _lblSummary;

        private CheckBox _chkCpuTemp;
        private CheckBox _chkMaxClock;
        private CheckBox _chkAvgClock;
        private CheckedListBox _clbCores;
        private Button _btnSelectAllCores;
        private Button _btnClearAllCores;

        private StripLine _thresholdStripLine;
        private float _threshold;
        private float _dpiScale = 1.0f;
        private ToolTip _chartToolTip;
        private string _lastHitSeries = null;
        private int _lastHitPointIndex = -1;

        private List<CpuCompositeLogEntry> _cachedData = new List<CpuCompositeLogEntry>();
        private bool _isUpdatingControls = false;

        // コア別クロック用の見分けやすいカラーパレット
        private static readonly Color[] CoreColors = new Color[]
        {
            Color.FromArgb(230, 126, 34),   // Orange
            Color.FromArgb(155, 89, 182),   // Purple
            Color.FromArgb(26, 188, 156),   // Turquoise
            Color.FromArgb(231, 76, 60),    // Light Red
            Color.FromArgb(52, 152, 219),   // Blue
            Color.FromArgb(241, 196, 15),   // Yellow
            Color.FromArgb(52, 73, 94),     // Dark Slate
            Color.FromArgb(211, 84, 0),     // Dark Orange
            Color.FromArgb(142, 68, 173),   // Dark Violet
            Color.FromArgb(22, 160, 133),   // Dark Cyan
            Color.FromArgb(192, 57, 43),    // Dark Red
            Color.FromArgb(41, 128, 185),   // Dark Blue
            Color.FromArgb(243, 156, 18),   // Dark Amber
            Color.FromArgb(127, 140, 141),  // Gray
            Color.FromArgb(39, 174, 96),    // Nephritis
            Color.FromArgb(189, 195, 199)   // Silver
        };

        public CpuCompositeGraphForm(float threshold)
        {
            _threshold = threshold;
            InitializeComponent();
            PopulateCoresList();
            LoadData();
        }

        private void InitializeComponent()
        {
            this.Text = "CPU クロック & 温度 複合推移グラフ (SQLite 履歴)";
            this.Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Icon = SystemIcons.Application;

            _dpiScale = UiHelper.GetDpiScale(this);

            this.ClientSize = new Size((int)(1100 * _dpiScale), (int)(660 * _dpiScale));
            this.MinimumSize = new Size((int)(800 * _dpiScale), (int)(500 * _dpiScale));

            int ctlHeight = (int)Math.Round(this.Font.Height * 1.35f);
            if (ctlHeight < (int)(24 * _dpiScale)) ctlHeight = (int)(24 * _dpiScale);

            // 1. 上部ツールバー
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
            _cmbRange.Items.AddRange(UiHelper.TimeRangePresets);
            _cmbRange.SelectedIndex = 0; // 直近1時間
            _cmbRange.Width = (int)(150 * _dpiScale);
            _cmbRange.Margin = new Padding(0, (int)(1 * _dpiScale), (int)(10 * _dpiScale), 0);
            _cmbRange.SelectedIndexChanged += OnRangeChanged;

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
            _dtpEnd.Margin = new Padding(0, (int)(1 * _dpiScale), (int)(10 * _dpiScale), 0);

            _btnApplyCustom = new Button();
            _btnApplyCustom.Text = "適用";
            _btnApplyCustom.Height = ctlHeight;
            _btnApplyCustom.Width = (int)(65 * _dpiScale);
            _btnApplyCustom.Visible = false;
            _btnApplyCustom.Margin = new Padding(0, 0, (int)(10 * _dpiScale), 0);
            _btnApplyCustom.Click += delegate { LoadData(); };

            Button btnRefresh = new Button();
            btnRefresh.Text = "再読み込み";
            btnRefresh.Height = ctlHeight;
            btnRefresh.Width = (int)(95 * _dpiScale);
            btnRefresh.Margin = new Padding(0, 0, (int)(12 * _dpiScale), 0);
            btnRefresh.Click += delegate { LoadData(); };

            _chkAutoRefresh = new CheckBox();
            _chkAutoRefresh.Text = "自動更新";
            _chkAutoRefresh.Checked = true;
            _chkAutoRefresh.AutoSize = true;
            _chkAutoRefresh.Margin = new Padding(0, (int)(4 * _dpiScale), (int)(10 * _dpiScale), 0);
            _chkAutoRefresh.CheckedChanged += delegate
            {
                if (_chkAutoRefresh.Checked)
                {
                    LoadData();
                }
            };

            toolPanel.Controls.Add(lblRange);
            toolPanel.Controls.Add(_cmbRange);
            toolPanel.Controls.Add(_dtpStart);
            toolPanel.Controls.Add(_lblTilde);
            toolPanel.Controls.Add(_dtpEnd);
            toolPanel.Controls.Add(_btnApplyCustom);
            toolPanel.Controls.Add(btnRefresh);
            toolPanel.Controls.Add(_chkAutoRefresh);

            // 2. 下部ステータスバー
            Panel bottomPanel = new Panel();
            bottomPanel.Dock = DockStyle.Bottom;
            bottomPanel.Height = (int)(32 * _dpiScale);
            bottomPanel.Padding = new Padding((int)(14 * _dpiScale), (int)(6 * _dpiScale), (int)(14 * _dpiScale), 0);

            _lblSummary = new Label();
            _lblSummary.Dock = DockStyle.Fill;
            _lblSummary.ForeColor = Color.FromArgb(100, 100, 100);
            _lblSummary.Text = "データを読み込み中...";
            bottomPanel.Controls.Add(_lblSummary);

            // 3. 左側: 系列選択パネル
            Panel leftPanel = new Panel();
            leftPanel.Dock = DockStyle.Left;
            leftPanel.Width = (int)(230 * _dpiScale);
            leftPanel.Padding = new Padding((int)(10 * _dpiScale), (int)(5 * _dpiScale), (int)(6 * _dpiScale), (int)(5 * _dpiScale));
            leftPanel.BackColor = Color.FromArgb(248, 249, 250);

            GroupBox grpGlobal = new GroupBox();
            grpGlobal.Text = "指標選択";
            grpGlobal.Dock = DockStyle.Top;
            grpGlobal.Height = (int)(125 * _dpiScale);
            grpGlobal.Padding = new Padding((int)(10 * _dpiScale));

            _chkMaxClock = new CheckBox();
            _chkMaxClock.Text = "全コア中 最高クロック [左軸]";
            _chkMaxClock.ForeColor = Color.FromArgb(2, 117, 216); // Blue
            _chkMaxClock.Font = new Font(this.Font, FontStyle.Bold);
            _chkMaxClock.Checked = true;
            _chkMaxClock.Dock = DockStyle.Top;
            _chkMaxClock.Height = (int)(28 * _dpiScale);
            _chkMaxClock.CheckedChanged += OnSeriesCheckChanged;

            _chkAvgClock = new CheckBox();
            _chkAvgClock.Text = "全コア 平均クロック [左軸]";
            _chkAvgClock.ForeColor = Color.FromArgb(92, 184, 92); // Green
            _chkAvgClock.Font = new Font(this.Font, FontStyle.Bold);
            _chkAvgClock.Checked = true;
            _chkAvgClock.Dock = DockStyle.Top;
            _chkAvgClock.Height = (int)(28 * _dpiScale);
            _chkAvgClock.CheckedChanged += OnSeriesCheckChanged;

            _chkCpuTemp = new CheckBox();
            _chkCpuTemp.Text = "CPU 最高温度 (℃) [右軸]";
            _chkCpuTemp.ForeColor = Color.FromArgb(217, 83, 79); // Crimson Red
            _chkCpuTemp.Font = new Font(this.Font, FontStyle.Bold);
            _chkCpuTemp.Checked = true;
            _chkCpuTemp.Dock = DockStyle.Top;
            _chkCpuTemp.Height = (int)(28 * _dpiScale);
            _chkCpuTemp.CheckedChanged += OnSeriesCheckChanged;

            grpGlobal.Controls.Add(_chkCpuTemp);
            grpGlobal.Controls.Add(_chkAvgClock);
            grpGlobal.Controls.Add(_chkMaxClock);

            GroupBox grpCores = new GroupBox();
            grpCores.Text = "コア別クロック [左軸]";
            grpCores.Dock = DockStyle.Fill;
            grpCores.Padding = new Padding((int)(10 * _dpiScale));

            Panel coreBtnPanel = new Panel();
            coreBtnPanel.Dock = DockStyle.Top;
            coreBtnPanel.Height = (int)(32 * _dpiScale);

            _btnSelectAllCores = new Button();
            _btnSelectAllCores.Text = "全選択";
            _btnSelectAllCores.Width = (int)(65 * _dpiScale);
            _btnSelectAllCores.Height = (int)(24 * _dpiScale);
            _btnSelectAllCores.Location = new Point(0, 0);
            _btnSelectAllCores.Click += delegate { SetAllCoresChecked(true); };

            _btnClearAllCores = new Button();
            _btnClearAllCores.Text = "全解除";
            _btnClearAllCores.Width = (int)(65 * _dpiScale);
            _btnClearAllCores.Height = (int)(24 * _dpiScale);
            _btnClearAllCores.Location = new Point((int)(72 * _dpiScale), 0);
            _btnClearAllCores.Click += delegate { SetAllCoresChecked(false); };

            coreBtnPanel.Controls.Add(_btnSelectAllCores);
            coreBtnPanel.Controls.Add(_btnClearAllCores);

            _clbCores = new CheckedListBox();
            _clbCores.Dock = DockStyle.Fill;
            _clbCores.CheckOnClick = true;
            _clbCores.IntegralHeight = false;
            _clbCores.ItemCheck += OnCoreItemCheck;

            grpCores.Controls.Add(_clbCores);
            grpCores.Controls.Add(coreBtnPanel);

            leftPanel.Controls.Add(grpCores);
            leftPanel.Controls.Add(grpGlobal);

            // 4. チャートコントロール
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

            // Primary Y軸（CPU クロック周波数 MHz - 本グラフのメイン基準軸）
            area.AxisY.Title = "CPU クロック周波数 (MHz)";
            area.AxisY.TitleFont = new Font("Segoe UI", 9.0f, FontStyle.Bold);
            area.AxisY.TitleForeColor = Color.FromArgb(2, 117, 216);
            area.AxisY.LabelStyle.Font = new Font("Segoe UI", 8.5f);
            area.AxisY.LabelStyle.ForeColor = Color.FromArgb(60, 60, 60);
            area.AxisY.MajorGrid.Interval = 1000;
            area.AxisY.MajorGrid.LineColor = Color.Silver;
            area.AxisY.MajorGrid.LineDashStyle = ChartDashStyle.Solid;
            area.AxisY.MajorGrid.LineWidth = 1;
            area.AxisY.MinorGrid.Enabled = true;
            area.AxisY.MinorGrid.Interval = 500;
            area.AxisY.MinorGrid.LineColor = Color.FromArgb(235, 238, 242);
            area.AxisY.MinorGrid.LineDashStyle = ChartDashStyle.Dot;
            area.AxisY.LineColor = Color.Silver;
            area.AxisY.Interval = 1000;

            // Secondary Y軸（CPU 温度 ℃ - 水平線はクロック周波数基準のためグリッド無効）
            area.AxisY2.Enabled = AxisEnabled.True;
            area.AxisY2.Title = "CPU 温度 (℃)";
            area.AxisY2.TitleFont = new Font("Segoe UI", 9.0f, FontStyle.Bold);
            area.AxisY2.TitleForeColor = Color.FromArgb(180, 40, 40);
            area.AxisY2.LabelStyle.Font = new Font("Segoe UI", 8.5f);
            area.AxisY2.LabelStyle.ForeColor = Color.FromArgb(60, 60, 60);
            area.AxisY2.MajorGrid.Enabled = false; // 水平線はクロック周波数基準とするため無効化
            area.AxisY2.MinorGrid.Enabled = false;
            area.AxisY2.LineColor = Color.Silver;
            area.AxisY2.Interval = 10;

            // 警告温度しきい値ライン (Secondary Y軸に配置)
            _thresholdStripLine = new StripLine();
            _thresholdStripLine.Interval = 0;
            _thresholdStripLine.IntervalOffset = _threshold;
            _thresholdStripLine.StripWidth = 0.0001;
            _thresholdStripLine.BorderColor = Color.FromArgb(220, 20, 20);
            _thresholdStripLine.BorderDashStyle = ChartDashStyle.Dash;
            _thresholdStripLine.BorderWidth = 1;
            _thresholdStripLine.Text = string.Format("警告閾値 ({0:F0}℃)", _threshold);
            _thresholdStripLine.TextAlignment = StringAlignment.Far;
            _thresholdStripLine.TextLineAlignment = StringAlignment.Near;
            _thresholdStripLine.ForeColor = Color.FromArgb(200, 20, 20);
            _thresholdStripLine.Font = new Font("Segoe UI", 8f, FontStyle.Bold);
            area.AxisY2.StripLines.Add(_thresholdStripLine);

            _chart.ChartAreas.Add(area);

            // 凡例
            Legend legend = new Legend("Default");
            legend.Docking = Docking.Top;
            legend.Alignment = StringAlignment.Far;
            legend.Font = new Font("Segoe UI", 9.0f);
            legend.BackColor = Color.Transparent;
            _chart.Legends.Add(legend);

            // インタラクティブ Tooltip 設定
            _chartToolTip = new ToolTip();
            _chartToolTip.InitialDelay = 50;
            _chartToolTip.ReshowDelay = 50;
            _chartToolTip.AutoPopDelay = 10000;

            _chart.MouseMove += OnChartMouseMove;
            _chart.MouseLeave += delegate
            {
                if (_chartToolTip != null) _chartToolTip.Hide(_chart);
                _chart.Cursor = Cursors.Default;
                _lastHitSeries = null;
                _lastHitPointIndex = -1;
            };

            this.Controls.Add(_chart);
            this.Controls.Add(leftPanel);
            this.Controls.Add(bottomPanel);
            this.Controls.Add(toolPanel);
        }

        private void PopulateCoresList()
        {
            HashSet<string> previouslyChecked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < _clbCores.CheckedItems.Count; i++)
            {
                previouslyChecked.Add(_clbCores.CheckedItems[i].ToString());
            }

            List<string> cores = HistoryDatabase.GetDistinctCpuCores();
            _isUpdatingControls = true;
            try
            {
                _clbCores.Items.Clear();
                for (int i = 0; i < cores.Count; i++)
                {
                    string core = cores[i];
                    _clbCores.Items.Add(core, previouslyChecked.Contains(core));
                }
            }
            finally
            {
                _isUpdatingControls = false;
            }
        }

        private void SetAllCoresChecked(bool isChecked)
        {
            _isUpdatingControls = true;
            for (int i = 0; i < _clbCores.Items.Count; i++)
            {
                _clbCores.SetItemChecked(i, isChecked);
            }
            _isUpdatingControls = false;
            RenderChart();
        }

        private void OnCoreItemCheck(object sender, ItemCheckEventArgs e)
        {
            if (_isUpdatingControls) return;
            this.BeginInvoke((MethodInvoker)delegate
            {
                RenderChart();
            });
        }

        private void OnSeriesCheckChanged(object sender, EventArgs e)
        {
            if (_isUpdatingControls) return;
            RenderChart();
        }

        private void OnRangeChanged(object sender, EventArgs e)
        {
            bool isCustom = (_cmbRange.SelectedIndex == UiHelper.CustomRangeIndex);
            _dtpStart.Visible = isCustom;
            _lblTilde.Visible = isCustom;
            _dtpEnd.Visible = isCustom;
            _btnApplyCustom.Visible = isCustom;

            if (!isCustom)
            {
                LoadData();
            }
        }

        public void OnLogWritten(CpuTemperatureSnapshot snapshot, float threshold)
        {
            if (this.IsDisposed) return;
            if (this.InvokeRequired)
            {
                this.BeginInvoke((MethodInvoker)delegate { OnLogWritten(snapshot, threshold); });
                return;
            }

            if (Math.Abs(_threshold - threshold) > 0.01f)
            {
                _threshold = threshold;
                if (_thresholdStripLine != null)
                {
                    _thresholdStripLine.IntervalOffset = _threshold;
                    _thresholdStripLine.Text = string.Format("警告閾値 ({0:F0}℃)", _threshold);
                }
            }

            if (this.Visible && _chkAutoRefresh.Checked)
            {
                // 新たなコアが検出されているか確認
                if (snapshot != null && snapshot.CpuClocks != null && snapshot.CpuClocks.Count > _clbCores.Items.Count)
                {
                    PopulateCoresList();
                }

                LoadData();
            }
        }

        public void LoadData()
        {
            DateTime? startTime;
            DateTime? endTime;
            UiHelper.ResolveTimeRange(_cmbRange.SelectedIndex, _dtpStart.Value, _dtpEnd.Value, out startTime, out endTime);

            _cachedData = HistoryDatabase.ReadCpuCompositeHistory(startTime, endTime);
            RenderChart();
        }

        private void RenderChart()
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

            _chart.Series.Clear();

            // 系列の定義
            Series seriesCpuTemp = null;
            Series seriesMaxClock = null;
            Series seriesAvgClock = null;
            Dictionary<string, Series> seriesCores = new Dictionary<string, Series>(StringComparer.OrdinalIgnoreCase);

            if (_chkMaxClock.Checked)
            {
                seriesMaxClock = CreateSeries("MaxClock", "全コア中 最高クロック", Color.FromArgb(2, 117, 216), AxisType.Primary, 1.2f, ChartDashStyle.Solid);
            }

            if (_chkAvgClock.Checked)
            {
                seriesAvgClock = CreateSeries("AvgClock", "全コア 平均クロック", Color.FromArgb(92, 184, 92), AxisType.Primary, 1.0f, ChartDashStyle.Dash);
            }

            for (int i = 0; i < _clbCores.Items.Count; i++)
            {
                if (_clbCores.GetItemChecked(i))
                {
                    string core = _clbCores.Items[i].ToString();
                    Color c = CoreColors[i % CoreColors.Length];
                    seriesCores[core] = CreateSeries("Core_" + core, core, c, AxisType.Primary, 1.0f, ChartDashStyle.Solid);
                }
            }

            if (_chkCpuTemp.Checked)
            {
                seriesCpuTemp = CreateSeries("CpuTemp", "CPU 最高温度", Color.FromArgb(217, 83, 79), AxisType.Secondary, 1.2f, ChartDashStyle.Solid);
            }

            // データのバインド
            float minTemp = 100f, maxTemp = 0f;
            float minClock = 10000f, maxClock = 0f;
            int pointCount = 0;

            for (int i = 0; i < _cachedData.Count; i++)
            {
                CpuCompositeLogEntry entry = _cachedData[i];
                DateTime dt = entry.Timestamp;

                if (seriesCpuTemp != null && entry.CpuTemperature.HasValue)
                {
                    float t = entry.CpuTemperature.Value;
                    seriesCpuTemp.Points.AddXY(dt, t);
                    if (t < minTemp) minTemp = t;
                    if (t > maxTemp) maxTemp = t;
                }

                if (seriesMaxClock != null && entry.MaxClock.HasValue)
                {
                    float clk = entry.MaxClock.Value;
                    seriesMaxClock.Points.AddXY(dt, clk);
                    if (clk < minClock) minClock = clk;
                    if (clk > maxClock) maxClock = clk;
                }

                if (seriesAvgClock != null && entry.AvgClock.HasValue)
                {
                    float clk = entry.AvgClock.Value;
                    seriesAvgClock.Points.AddXY(dt, clk);
                    if (clk < minClock) minClock = clk;
                    if (clk > maxClock) maxClock = clk;
                }

                foreach (var kvp in seriesCores)
                {
                    float cVal;
                    if (entry.CoreClocks.TryGetValue(kvp.Key, out cVal))
                    {
                        kvp.Value.Points.AddXY(dt, cVal);
                        if (cVal < minClock) minClock = cVal;
                        if (cVal > maxClock) maxClock = cVal;
                    }
                }

                pointCount++;
            }

            ChartArea area = _chart.ChartAreas["Default"];

            // Primary Y軸（クロック MHz）のスケーリング (1000MHz毎の基準水平線)
            if (maxClock > 0f)
            {
                float clkMax = (float)Math.Ceiling(maxClock / 1000.0) * 1000f;
                float clkMin = Math.Max(0f, (float)Math.Floor(minClock / 1000.0) * 1000f);
                if (clkMin >= clkMax) clkMin = Math.Max(0f, clkMax - 1000f);
                area.AxisY.Minimum = clkMin;
                area.AxisY.Maximum = clkMax;
                area.AxisY.Interval = 1000;
                area.AxisY.MajorGrid.Interval = 1000;
            }
            else
            {
                area.AxisY.Minimum = 0;
                area.AxisY.Maximum = 6000;
                area.AxisY.Interval = 1000;
                area.AxisY.MajorGrid.Interval = 1000;
            }

            // Secondary Y軸（温度 ℃）のスケーリング (グリッド無効)
            if (maxTemp > 0f)
            {
                float yMax = Math.Max(_threshold + 10f, (float)Math.Ceiling((maxTemp + 5f) / 10.0) * 10f);
                float yMin = Math.Max(0f, (float)Math.Floor((minTemp - 5f) / 10.0) * 10f);
                if (yMin >= yMax) yMin = Math.Max(0f, yMax - 20f);
                area.AxisY2.Minimum = yMin;
                area.AxisY2.Maximum = yMax;
                area.AxisY2.Interval = 10;
            }
            else
            {
                area.AxisY2.Minimum = 20;
                area.AxisY2.Maximum = 100;
                area.AxisY2.Interval = 10;
            }

            // X軸フォーマット調整 (24時間以上なら日付付き)
            DateTime? firstTime = _cachedData.Count >= 2 ? (DateTime?)_cachedData[0].Timestamp : null;
            DateTime? lastTime = _cachedData.Count >= 2 ? (DateTime?)_cachedData[_cachedData.Count - 1].Timestamp : null;
            UiHelper.UpdateChartTimeAxisFormat(area.AxisX, firstTime, lastTime, _cmbRange.SelectedIndex);

            // サマリーテキストの更新
            if (pointCount == 0)
            {
                _lblSummary.Text = "指定された期間のログデータが存在しません。";
            }
            else
            {
                CpuCompositeLogEntry latest = _cachedData[_cachedData.Count - 1];
                string maxClkStr = latest.MaxClock.HasValue ? string.Format("{0:F0}MHz", latest.MaxClock.Value) : "N/A";
                string avgClkStr = latest.AvgClock.HasValue ? string.Format("{0:F0}MHz", latest.AvgClock.Value) : "N/A";
                string tempStr = latest.CpuTemperature.HasValue ? string.Format("{0:F1}℃", latest.CpuTemperature.Value) : "N/A";

                _lblSummary.Text = string.Format(
                    "データ点数: {0} 件 | 最新: [全コア最高: {1}, 全コア平均: {2}, CPU温度: {3}] (最新記録日時: {4:yyyy/MM/dd HH:mm:ss})",
                    pointCount, maxClkStr, avgClkStr, tempStr, latest.Timestamp);
            }
        }

        private Series CreateSeries(string name, string legendText, Color color, AxisType axisType, float lineWidth, ChartDashStyle dashStyle)
        {
            Series s = new Series(name);
            s.ChartType = SeriesChartType.Line;
            s.XValueType = ChartValueType.DateTime;
            s.YAxisType = axisType;
            s.Color = color;
            int width = (int)Math.Round(lineWidth);
            if (width < 1) width = 1;
            s.BorderWidth = width;
            s.BorderDashStyle = dashStyle;
            s.LegendText = legendText;
            s.IsVisibleInLegend = true;
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

            Series series = _chart.Series[hitSeriesName];
            DataPoint dp = series.Points[pointIndex];
            DateTime time = DateTime.FromOADate(dp.XValue);
            double val = dp.YValues[0];
            string unit = series.YAxisType == AxisType.Primary ? "MHz" : "℃";
            string valFormat = series.YAxisType == AxisType.Primary ? "F0" : "F1";

            string tipText = string.Format("{0:yyyy/MM/dd HH:mm:ss}\n{1}: {2} {3}",
                time,
                series.LegendText,
                val.ToString(valFormat),
                unit);

            UiHelper.ShowChartTooltip(_chartToolTip, _chart, e.Location, tipText, _dpiScale);
        }

        private bool FindNearestPoint(Point mouseLocation, float maxPixelDistance, out string outSeriesName, out int outPointIndex)
        {
            outSeriesName = null;
            outPointIndex = -1;
            if (_chart == null || _chart.Series.Count == 0 || _chart.ChartAreas.Count == 0) return false;
            ChartArea area = _chart.ChartAreas["Default"];
            if (area == null) return false;

            HitTestResult hit = _chart.HitTest(mouseLocation.X, mouseLocation.Y, ChartElementType.DataPoint);
            if (hit != null && hit.ChartElementType == ChartElementType.DataPoint && hit.Series != null && hit.PointIndex >= 0)
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
                    Axis yAxis = s.YAxisType == AxisType.Secondary ? area.AxisY2 : area.AxisY;

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
                        double py = yAxis.ValueToPixelPosition(pt.YValues[0]);
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

