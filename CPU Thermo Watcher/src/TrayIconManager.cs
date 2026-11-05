// --------------------------------------------------------------------------------------------------------------------
// <copyright file="TrayIconManager.cs" company="CPU Thermo Watcher Contributors">
//   Copyright (c) 2026, CPU Thermo Watcher Contributors. All rights reserved.
//   Licensed under the 3-Clause BSD License.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CpuThermoWatcher
{
    public class TrayIconManager : IDisposable
    {
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool DestroyIcon(IntPtr handle);

        private class TrayIconItem
        {
            public string DeviceId;
            public NotifyIcon NotifyIcon;
            public IntPtr CurrentIconHandle;
        }

        private Dictionary<string, TrayIconItem> _trayIcons = new Dictionary<string, TrayIconItem>(StringComparer.OrdinalIgnoreCase);
        private ContextMenuStrip _contextMenu;
        private ToolStripMenuItem _headerItem;
        private ToolStripMenuItem _eventLogToggleItem;

        private Settings _settings;
        private HardwareMonitor _hardwareMonitor;
        private EventLogger _eventLogger;
        private Timer _timer;

        private CpuTemperatureSnapshot _latestSnapshot;
        private SensorListForm _sensorListForm;
        private GraphViewerForm _graphViewerForm;
        private HistoryViewerForm _historyViewerForm;

        public TrayIconManager(Settings settings, HardwareMonitor hardwareMonitor, EventLogger eventLogger)
        {
            _settings = settings;
            _hardwareMonitor = hardwareMonitor;
            _eventLogger = eventLogger;

            InitializeContextMenu();

            _timer = new Timer();
            _timer.Interval = _settings.UpdateIntervalMs;
            _timer.Tick += OnTimerTick;
            _timer.Start();

            // 初回更新
            UpdateTemperature();
        }

        private void InitializeContextMenu()
        {
            _contextMenu = new ContextMenuStrip();
            _contextMenu.Font = new Font("Segoe UI", 9f);

            _headerItem = new ToolStripMenuItem("CPU / GPU: 計測中...");
            _headerItem.Enabled = false;
            _headerItem.Font = new Font(_contextMenu.Font, FontStyle.Bold);
            _contextMenu.Items.Add(_headerItem);

            _contextMenu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem sensorListItem = new ToolStripMenuItem("センサー温度一覧 (&S)...");
            sensorListItem.Click += OnShowSensorList;
            _contextMenu.Items.Add(sensorListItem);

            ToolStripMenuItem graphItem = new ToolStripMenuItem("温度推移グラフを表示 (&G)...");
            graphItem.Click += OnShowGraph;
            _contextMenu.Items.Add(graphItem);

            ToolStripMenuItem historyItem = new ToolStripMenuItem("温度履歴データ一覧 (&H)...");
            historyItem.Click += OnShowHistory;
            _contextMenu.Items.Add(historyItem);

            _contextMenu.Items.Add(new ToolStripSeparator());

            _eventLogToggleItem = new ToolStripMenuItem("Windows イベントログに記録 (&L)");
            _eventLogToggleItem.CheckOnClick = true;
            _eventLogToggleItem.Checked = _settings.EnableEventLogging;
            _eventLogToggleItem.CheckedChanged += OnToggleEventLogging;
            _contextMenu.Items.Add(_eventLogToggleItem);

            ToolStripMenuItem settingsItem = new ToolStripMenuItem("設定 (&C)...");
            settingsItem.Click += OnShowSettings;
            _contextMenu.Items.Add(settingsItem);

            _contextMenu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem refreshItem = new ToolStripMenuItem("今すぐ更新 (&R)");
            refreshItem.Click += delegate { UpdateTemperature(); };
            _contextMenu.Items.Add(refreshItem);

            ToolStripMenuItem exitItem = new ToolStripMenuItem("終了 (&X)");
            exitItem.Click += delegate { Application.Exit(); };
            _contextMenu.Items.Add(exitItem);
        }

        private void OnTimerTick(object sender, EventArgs e)
        {
            UpdateTemperature();
        }

        public void UpdateTemperature()
        {
            CpuTemperatureSnapshot snapshot = _hardwareMonitor.ReadCpuTemperatures();
            _latestSnapshot = snapshot;

            // 1. SQLite への温度記録 (定期間隔または高温警告時)
            bool sqliteWritten = HistoryDatabase.LogSnapshot(snapshot, _settings);

            // 2. Windows イベントログへの記録 (設定有効時または高温警告時)
            bool logWritten = _eventLogger.LogSnapshot(snapshot, _settings);

            // 3. デバイス一覧のトレイアイコン同期
            List<HardwareDeviceSummary> devices = snapshot.Devices;
            if (devices == null || devices.Count == 0)
            {
                devices = new List<HardwareDeviceSummary>();
                if (snapshot.MaxCpuSensor != null)
                {
                    devices.Add(new HardwareDeviceSummary("CPU", DeviceCategory.Cpu, snapshot.MaxCpuSensor.HardwareName, "CPU", snapshot.MaxCpuSensor));
                }
            }

            // 全検出デバイスおよび設定対象デバイスを巡回
            HashSet<string> allActiveDeviceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < devices.Count; i++)
            {
                allActiveDeviceIds.Add(devices[i].DeviceId);
            }
            if (_settings.EnabledTrayDeviceIds != null)
            {
                for (int i = 0; i < _settings.EnabledTrayDeviceIds.Count; i++)
                {
                    allActiveDeviceIds.Add(_settings.EnabledTrayDeviceIds[i]);
                }
            }

            foreach (string devId in allActiveDeviceIds)
            {
                bool showInTray = _settings.IsDeviceInTray(devId);
                HardwareDeviceSummary devSummary = snapshot.GetDevice(devId);

                if (showInTray)
                {
                    TrayIconItem item = EnsureTrayIconItem(devId);
                    item.NotifyIcon.Visible = true;

                    if (devSummary != null && devSummary.MaxSensor != null)
                    {
                        float temp = devSummary.MaxSensor.Value;
                        float threshold = _settings.GetWarningThreshold(devId);
                        bool isWarning = temp >= threshold;

                        UpdateIconBitmap(item, devSummary.Category, devId, temp, isWarning);

                        string tooltip = string.Format("[{0}] {1:F0}℃ ({2})\n警告閾値: {3:F0}℃",
                            devId.Replace(":", " #"), temp, devSummary.MaxSensor.SensorName, threshold);
                        if (tooltip.Length > 63) tooltip = tooltip.Substring(0, 60) + "...";
                        item.NotifyIcon.Text = tooltip;
                    }
                    else
                    {
                        UpdateIconNA(item, devSummary != null ? devSummary.Category : DeviceCategory.Cpu, devId);
                        item.NotifyIcon.Text = string.Format("[{0}] 温度取得待機中", devId.Replace(":", " #"));
                    }
                }
                else
                {
                    if (_trayIcons.ContainsKey(devId))
                    {
                        _trayIcons[devId].NotifyIcon.Visible = false;
                    }
                }
            }

            // 4. コンテキストメニューヘッダーの更新
            List<string> headerParts = new List<string>();
            for (int i = 0; i < devices.Count; i++)
            {
                HardwareDeviceSummary d = devices[i];
                if (d.MaxSensor != null)
                {
                    headerParts.Add(string.Format("{0}: {1:F1}℃", d.DeviceId.Replace(":", " #"), d.MaxSensor.Value));
                }
            }
            if (headerParts.Count > 0)
            {
                _headerItem.Text = string.Join(" | ", headerParts.ToArray());
            }
            else
            {
                _headerItem.Text = "CPU / GPU: 待機中...";
            }

            // 5. センサー一覧ウィンドウが開いていれば更新通知
            if (_sensorListForm != null && !_sensorListForm.IsDisposed && _sensorListForm.Visible)
            {
                _sensorListForm.UpdateSnapshot(snapshot, _settings.WarningThresholdCelsius);
            }

            // 6. グラフウィンドウが開いていれば、新データ記録後に自動再描画を通知
            if ((sqliteWritten || logWritten) && _graphViewerForm != null && !_graphViewerForm.IsDisposed && _graphViewerForm.Visible)
            {
                _graphViewerForm.OnLogWritten(_settings.WarningThresholdCelsius);
            }

            // 7. 履歴データビューワーが開いていれば、新データ記録後に更新通知
            if ((sqliteWritten || logWritten) && _historyViewerForm != null && !_historyViewerForm.IsDisposed && _historyViewerForm.Visible)
            {
                _historyViewerForm.OnLogWritten();
            }
        }

        private TrayIconItem EnsureTrayIconItem(string deviceId)
        {
            if (_trayIcons.ContainsKey(deviceId))
            {
                return _trayIcons[deviceId];
            }

            TrayIconItem item = new TrayIconItem();
            item.DeviceId = deviceId;
            item.NotifyIcon = new NotifyIcon();
            item.NotifyIcon.ContextMenuStrip = _contextMenu;
            item.NotifyIcon.DoubleClick += OnShowSensorList;
            _trayIcons[deviceId] = item;
            return item;
        }

        private void UpdateIconBitmap(TrayIconItem item, DeviceCategory category, string deviceId, float temp, bool isWarning)
        {
            int iconSize = 32;
            int displayVal = (int)Math.Round(temp);
            string text = displayVal.ToString();

            using (Bitmap bitmap = new Bitmap(iconSize, iconSize))
            {
                using (Graphics g = Graphics.FromImage(bitmap))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

                    if (isWarning)
                    {
                        using (Brush bgBrush = new SolidBrush(Color.FromArgb(220, 20, 20)))
                        {
                            g.FillRectangle(bgBrush, 0, 0, iconSize, iconSize);
                        }
                        DrawTextCentered(g, text, Color.White, Color.Transparent, iconSize);
                    }
                    else
                    {
                        g.Clear(Color.Transparent);

                        Color textColor;
                        Color outlineColor;

                        if (category == DeviceCategory.Cpu)
                        {
                            bool isWhiteText = ShouldUseWhiteText(_settings.TextColorMode);
                            textColor = isWhiteText ? Color.White : Color.Black;
                            outlineColor = isWhiteText ? Color.FromArgb(200, 0, 0, 0) : Color.FromArgb(200, 255, 255, 255);
                        }
                        else if (category == DeviceCategory.IGpu)
                        {
                            // iGPU はエメラルドグリーン
                            textColor = Color.FromArgb(100, 235, 130);
                            outlineColor = Color.FromArgb(220, 0, 0, 0);
                        }
                        else
                        {
                            // dGPU は橙 / コーラルピンク
                            if (string.Equals(deviceId, "dGPU:0", StringComparison.OrdinalIgnoreCase))
                            {
                                textColor = Color.FromArgb(255, 185, 55); // アンバー
                            }
                            else
                            {
                                textColor = Color.FromArgb(255, 120, 140); // コーラルピンク
                            }
                            outlineColor = Color.FromArgb(220, 0, 0, 0);
                        }

                        DrawTextCentered(g, text, textColor, outlineColor, iconSize);
                    }
                }
                SetItemIcon(item, bitmap);
            }
        }

        private void UpdateIconNA(TrayIconItem item, DeviceCategory category, string deviceId)
        {
            int iconSize = 32;
            using (Bitmap bitmap = new Bitmap(iconSize, iconSize))
            {
                using (Graphics g = Graphics.FromImage(bitmap))
                {
                    g.Clear(Color.Transparent);
                    Color col = (category == DeviceCategory.Cpu) ? Color.Gray :
                                (category == DeviceCategory.IGpu) ? Color.FromArgb(80, 160, 90) : Color.FromArgb(190, 140, 70);
                    DrawTextCentered(g, "--", col, Color.Black, iconSize);
                }
                SetItemIcon(item, bitmap);
            }
        }

        private void SetItemIcon(TrayIconItem item, Bitmap bitmap)
        {
            IntPtr hIcon = bitmap.GetHicon();
            Icon newIcon = Icon.FromHandle(hIcon);
            item.NotifyIcon.Icon = newIcon;

            if (item.CurrentIconHandle != IntPtr.Zero)
            {
                DestroyIcon(item.CurrentIconHandle);
            }
            item.CurrentIconHandle = hIcon;
        }

        private static bool ShouldUseWhiteText(string textColorMode)
        {
            if (string.Equals(textColorMode, "White", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            if (string.Equals(textColorMode, "Black", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            return IsSystemDarkTheme();
        }

        private static void DrawTextCentered(Graphics g, string text, Color textColor, Color outlineColor, int size)
        {
            float fontSize = text.Length >= 3 ? 12.0f : 17.0f;
            using (Font font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                using (StringFormat sf = new StringFormat())
                {
                    sf.Alignment = StringAlignment.Center;
                    sf.LineAlignment = StringAlignment.Center;

                    RectangleF rect = new RectangleF(0, 0, size, size);

                    if (outlineColor != Color.Transparent)
                    {
                        using (GraphicsPath path = new GraphicsPath())
                        {
                            path.AddString(text, font.FontFamily, (int)font.Style, font.Size, rect, sf);
                            using (Pen pen = new Pen(outlineColor, 3f))
                            {
                                pen.LineJoin = LineJoin.Round;
                                g.DrawPath(pen, path);
                            }
                            using (Brush brush = new SolidBrush(textColor))
                            {
                                g.FillPath(brush, path);
                            }
                        }
                    }
                    else
                    {
                        using (Brush brush = new SolidBrush(textColor))
                        {
                            g.DrawString(text, font, brush, rect, sf);
                        }
                    }
                }
            }
        }

        private void OnShowSensorList(object sender, EventArgs e)
        {
            if (_sensorListForm == null || _sensorListForm.IsDisposed)
            {
                _sensorListForm = new SensorListForm(_latestSnapshot, _settings.WarningThresholdCelsius);
            }
            _sensorListForm.Show();
            _sensorListForm.BringToFront();
        }

        private void OnShowGraph(object sender, EventArgs e)
        {
            if (_graphViewerForm == null || _graphViewerForm.IsDisposed)
            {
                _graphViewerForm = new GraphViewerForm(_settings.WarningThresholdCelsius);
            }
            _graphViewerForm.Show();
            _graphViewerForm.BringToFront();
        }

        private void OnShowHistory(object sender, EventArgs e)
        {
            if (_historyViewerForm == null || _historyViewerForm.IsDisposed)
            {
                _historyViewerForm = new HistoryViewerForm();
            }
            _historyViewerForm.Show();
            _historyViewerForm.BringToFront();
        }

        private void OnToggleEventLogging(object sender, EventArgs e)
        {
            _settings.EnableEventLogging = _eventLogToggleItem.Checked;
            _settings.Save();
        }

        private void OnShowSettings(object sender, EventArgs e)
        {
            using (SettingsForm form = new SettingsForm(_settings, _latestSnapshot))
            {
                if (form.ShowDialog() == DialogResult.OK)
                {
                    _settings.Save();
                    _timer.Interval = _settings.UpdateIntervalMs;
                    _eventLogToggleItem.Checked = _settings.EnableEventLogging;
                    UpdateTemperature();
                }
            }
        }

        public void Dispose()
        {
            if (_timer != null)
            {
                _timer.Stop();
                _timer.Dispose();
                _timer = null;
            }

            foreach (TrayIconItem item in _trayIcons.Values)
            {
                if (item.NotifyIcon != null)
                {
                    item.NotifyIcon.Visible = false;
                    item.NotifyIcon.Dispose();
                    item.NotifyIcon = null;
                }
                if (item.CurrentIconHandle != IntPtr.Zero)
                {
                    DestroyIcon(item.CurrentIconHandle);
                    item.CurrentIconHandle = IntPtr.Zero;
                }
            }
            _trayIcons.Clear();

            if (_sensorListForm != null && !_sensorListForm.IsDisposed)
            {
                _sensorListForm.Dispose();
                _sensorListForm = null;
            }

            if (_graphViewerForm != null && !_graphViewerForm.IsDisposed)
            {
                _graphViewerForm.Dispose();
                _graphViewerForm = null;
            }

            if (_historyViewerForm != null && !_historyViewerForm.IsDisposed)
            {
                _historyViewerForm.Dispose();
                _historyViewerForm = null;
            }

            if (_contextMenu != null)
            {
                _contextMenu.Dispose();
                _contextMenu = null;
            }
        }

        private static bool IsSystemDarkTheme()
        {
            try
            {
                using (Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    if (key != null)
                    {
                        object val = key.GetValue("SystemUsesLightTheme");
                        if (val != null)
                        {
                            return Convert.ToInt32(val) == 0;
                        }
                    }
                }
            }
            catch
            {
            }
            return true;
        }
    }
}
