// --------------------------------------------------------------------------------------------------------------------
// <copyright file="EventLogger.cs" company="CPU Thermo Watcher Contributors">
//   Copyright (c) 2026, CPU Thermo Watcher Contributors. All rights reserved.
//   Licensed under the 3-Clause BSD License.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CpuThermoWatcher
{
    public class TemperatureLogEntry
    {
        public DateTime Timestamp { get; set; }
        public float MaxTemperature { get; set; } // CPU 最高温度
        public string MaxSensorName { get; set; }
        public float? IGpuTemperature { get; set; }
        public string IGpuSensorName { get; set; }
        public Dictionary<string, float> DGpuTemperatures { get; set; }
        public Dictionary<string, string> DGpuSensorNames { get; set; }
        public float? GpuTemperature { get; set; } // 旧互換用 (代表GPU温度)
        public string GpuSensorName { get; set; }
        public bool IsWarning { get; set; }

        public TemperatureLogEntry()
        {
            DGpuTemperatures = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            DGpuSensorNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public class EventLogger
    {
        public const string SourceName = "CPU Thermo Watcher";
        public const string LogName = "CPU Thermo Watcher";
        public const string LegacySourceName = "CPUThermoWatcher";

        public const int EventIdRegular = 1001;
        public const int EventIdWarning = 1002;

        public const short EventCategoryRegular = 1;
        public const short EventCategoryWarning = 2;

        private static readonly Regex CpuTempRegex = new Regex(@"(?:CPU\s+)?(?:Max\s*)?Temperature:\s*([0-9]+\.?[0-9]*)", RegexOptions.IgnoreCase);
        private static readonly Regex CpuSensorRegex = new Regex(@"(?:CPU\s+)?(?:Max\s*)?Sensor:\s*(.+)", RegexOptions.IgnoreCase);
        private static readonly Regex IGpuTempRegex = new Regex(@"iGPU\s*(?:Max\s*)?Temperature:\s*([0-9]+\.?[0-9]*)", RegexOptions.IgnoreCase);
        private static readonly Regex IGpuSensorRegex = new Regex(@"iGPU\s*(?:Max\s*)?Sensor:\s*(.+)", RegexOptions.IgnoreCase);
        private static readonly Regex DGpuTempRegex = new Regex(@"(dGPU[:#]?[0-9]*)\s*(?:Max\s*)?Temperature:\s*([0-9]+\.?[0-9]*)", RegexOptions.IgnoreCase);
        private static readonly Regex DGpuSensorRegex = new Regex(@"(dGPU[:#]?[0-9]*)\s*(?:Max\s*)?Sensor:\s*(.+)", RegexOptions.IgnoreCase);
        private static readonly Regex LegacyGpuTempRegex = new Regex(@"GPU\s*(?:Max\s*)?Temperature:\s*([0-9]+\.?[0-9]*)", RegexOptions.IgnoreCase);
        private static readonly Regex LegacyGpuSensorRegex = new Regex(@"GPU\s*(?:Max\s*)?Sensor:\s*(.+)", RegexOptions.IgnoreCase);

        private DateTime _lastRegularLogTime = DateTime.MinValue;
        private DateTime _lastWarningLogTime = DateTime.MinValue;

        public EventLogger()
        {
            EnsureSourceExists();
        }

        private static void EnsureSourceExists()
        {
            try
            {
                if (EventLog.SourceExists(SourceName))
                {
                    string currentLog = EventLog.LogNameFromSourceName(SourceName, ".");
                    if (!string.Equals(currentLog, LogName, StringComparison.OrdinalIgnoreCase))
                    {
                        EventLog.DeleteEventSource(SourceName);
                        System.Threading.Thread.Sleep(200);
                        EventLog.CreateEventSource(SourceName, LogName);
                    }
                }
                else
                {
                    EventLog.CreateEventSource(SourceName, LogName);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("EventLog.EnsureSourceExists failed: " + ex.Message);
            }
        }

        public bool LogSnapshot(CpuTemperatureSnapshot snapshot, Settings settings)
        {
            if (snapshot == null || !snapshot.Success || snapshot.Sensors.Count == 0)
            {
                return false;
            }

            if (!settings.EnableEventLogging)
            {
                return false;
            }

            DateTime now = DateTime.Now;
            bool cpuWarning = snapshot.MaxCpuSensor != null && snapshot.MaxCpuSensor.Value >= settings.WarningThresholdCelsius;
            bool igpuWarning = snapshot.MaxIGpuSensor != null && snapshot.MaxIGpuSensor.Value >= settings.WarningThresholdIGpuCelsius;
            bool dgpuWarning = false;

            if (snapshot.Devices != null)
            {
                for (int i = 0; i < snapshot.Devices.Count; i++)
                {
                    HardwareDeviceSummary dev = snapshot.Devices[i];
                    if (dev.Category == DeviceCategory.DGpu && dev.MaxSensor != null)
                    {
                        if (dev.MaxSensor.Value >= settings.WarningThresholdDGpuCelsius)
                        {
                            dgpuWarning = true;
                            break;
                        }
                    }
                }
            }

            bool isOverThreshold = cpuWarning || igpuWarning || dgpuWarning;

            // 1. 高温警告時のログ出力（最低30秒間隔）
            if (isOverThreshold && (now - _lastWarningLogTime).TotalSeconds >= 30)
            {
                bool written = WriteLog(snapshot, isWarning: true, settings: settings);
                _lastWarningLogTime = now;
                _lastRegularLogTime = now;
                return written;
            }

            // 2. 定期サンプリングログ出力
            if ((now - _lastRegularLogTime).TotalSeconds >= settings.EventLogIntervalSeconds)
            {
                bool written = WriteLog(snapshot, isWarning: false, settings: settings);
                _lastRegularLogTime = now;
                return written;
            }

            return false;
        }

        private static bool WriteLog(CpuTemperatureSnapshot snapshot, bool isWarning, Settings settings)
        {
            try
            {
                EnsureSourceExists();

                StringBuilder sb = new StringBuilder();
                sb.AppendLine("[CPU Thermo Watcher]");
                sb.AppendLine(string.Format("Timestamp: {0:yyyy-MM-dd HH:mm:ss}", snapshot.Timestamp));

                // CPU
                if (snapshot.MaxCpuSensor != null)
                {
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "CPU Max Temperature: {0:F1} C", snapshot.MaxCpuSensor.Value));
                    sb.AppendLine(string.Format("CPU Max Sensor: {0} ({1})", snapshot.MaxCpuSensor.SensorName, snapshot.MaxCpuSensor.HardwareName));
                    // 旧互換用
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "Max Temperature: {0:F1} C", snapshot.MaxCpuSensor.Value));
                    sb.AppendLine(string.Format("Max Sensor: {0} ({1})", snapshot.MaxCpuSensor.SensorName, snapshot.MaxCpuSensor.HardwareName));
                }

                // iGPU
                if (snapshot.MaxIGpuSensor != null)
                {
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "iGPU Temperature: {0:F1} C", snapshot.MaxIGpuSensor.Value));
                    sb.AppendLine(string.Format("iGPU Sensor: {0} ({1})", snapshot.MaxIGpuSensor.SensorName, snapshot.MaxIGpuSensor.HardwareName));
                }

                // 各 dGPU
                if (snapshot.Devices != null)
                {
                    for (int i = 0; i < snapshot.Devices.Count; i++)
                    {
                        HardwareDeviceSummary dev = snapshot.Devices[i];
                        if (dev.Category == DeviceCategory.DGpu && dev.MaxSensor != null)
                        {
                            sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0} Temperature: {1:F1} C", dev.DeviceId, dev.MaxSensor.Value));
                            sb.AppendLine(string.Format("{0} Sensor: {1} ({2})", dev.DeviceId, dev.MaxSensor.SensorName, dev.DeviceName));
                        }
                    }
                }

                // 全体 GPU 最高温度 (旧互換用)
                if (snapshot.MaxGpuSensor != null)
                {
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "GPU Temperature: {0:F1} C", snapshot.MaxGpuSensor.Value));
                    sb.AppendLine(string.Format("GPU Sensor: {0} ({1})", snapshot.MaxGpuSensor.SensorName, snapshot.MaxGpuSensor.HardwareName));
                }

                if (isWarning)
                {
                    StringBuilder warnSb = new StringBuilder("Status: WARNING (");
                    if (snapshot.MaxCpuSensor != null && snapshot.MaxCpuSensor.Value >= settings.WarningThresholdCelsius)
                    {
                        warnSb.Append(string.Format(CultureInfo.InvariantCulture, "CPU: {0:F1} C >= {1:F0} C; ", snapshot.MaxCpuSensor.Value, settings.WarningThresholdCelsius));
                    }
                    if (snapshot.MaxIGpuSensor != null && snapshot.MaxIGpuSensor.Value >= settings.WarningThresholdIGpuCelsius)
                    {
                        warnSb.Append(string.Format(CultureInfo.InvariantCulture, "iGPU: {0:F1} C >= {1:F0} C; ", snapshot.MaxIGpuSensor.Value, settings.WarningThresholdIGpuCelsius));
                    }
                    if (snapshot.Devices != null)
                    {
                        for (int i = 0; i < snapshot.Devices.Count; i++)
                        {
                            HardwareDeviceSummary dev = snapshot.Devices[i];
                            if (dev.Category == DeviceCategory.DGpu && dev.MaxSensor != null && dev.MaxSensor.Value >= settings.WarningThresholdDGpuCelsius)
                            {
                                warnSb.Append(string.Format(CultureInfo.InvariantCulture, "{0}: {1:F1} C >= {2:F0} C; ", dev.DeviceId, dev.MaxSensor.Value, settings.WarningThresholdDGpuCelsius));
                            }
                        }
                    }
                    sb.AppendLine(warnSb.ToString().TrimEnd(' ', ';') + ")");
                }
                else
                {
                    sb.AppendLine("Status: Normal");
                }

                sb.AppendLine();
                sb.AppendLine("=== All Sensors ===");
                foreach (TemperatureReading reading in snapshot.Sensors)
                {
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "- [{0}] {1} | {2}: {3:F1} C", reading.DeviceId, reading.HardwareName, reading.SensorName, reading.Value));
                }

                string message = sb.ToString();
                EventLogEntryType entryType = isWarning ? EventLogEntryType.Warning : EventLogEntryType.Information;
                int eventId = isWarning ? EventIdWarning : EventIdRegular;
                short category = isWarning ? EventCategoryWarning : EventCategoryRegular;

                using (EventLog log = new EventLog(LogName))
                {
                    log.Source = SourceName;
                    log.WriteEntry(message, entryType, eventId, category);
                }
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("EventLog.WriteEntry failed: " + ex.Message);
                return false;
            }
        }

        public static List<TemperatureLogEntry> ReadLogs(TimeSpan? timeRange)
        {
            EnsureSourceExists();

            DateTime cutoff = timeRange.HasValue ? DateTime.Now.Subtract(timeRange.Value) : DateTime.MinValue;
            Dictionary<DateTime, TemperatureLogEntry> dict = new Dictionary<DateTime, TemperatureLogEntry>();

            // 1. 専用ログから読み込み
            ReadEntriesFromLog(LogName, cutoff, timeRange, dict);

            // 2. 旧 Application ログの履歴も読み込み
            if (!string.Equals(LogName, "Application", StringComparison.OrdinalIgnoreCase))
            {
                ReadEntriesFromLog("Application", cutoff, timeRange, dict);
            }

            List<TemperatureLogEntry> list = new List<TemperatureLogEntry>(dict.Values);
            list.Sort(delegate(TemperatureLogEntry a, TemperatureLogEntry b)
            {
                return a.Timestamp.CompareTo(b.Timestamp);
            });

            return list;
        }

        private static void ReadEntriesFromLog(string logName, DateTime cutoff, TimeSpan? timeRange, Dictionary<DateTime, TemperatureLogEntry> dict)
        {
            try
            {
                if (!EventLog.Exists(logName))
                {
                    return;
                }

                using (EventLog log = new EventLog(logName))
                {
                    EventLogEntryCollection entries = log.Entries;
                    int count = entries.Count;

                    for (int i = count - 1; i >= 0; i--)
                    {
                        try
                        {
                            EventLogEntry entry = entries[i];
                            bool matchesSource = string.Equals(entry.Source, SourceName, StringComparison.OrdinalIgnoreCase) ||
                                                 string.Equals(entry.Source, LegacySourceName, StringComparison.OrdinalIgnoreCase);
                            if (!matchesSource)
                            {
                                continue;
                            }

                            if (entry.TimeGenerated < cutoff)
                            {
                                if (timeRange.HasValue)
                                {
                                    break;
                                }
                                continue;
                            }

                            if (dict.ContainsKey(entry.TimeGenerated))
                            {
                                continue;
                            }

                            string msg = entry.Message;
                            Match matchTemp = CpuTempRegex.Match(msg);
                            if (matchTemp.Success)
                            {
                                float temp;
                                if (float.TryParse(matchTemp.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out temp))
                                {
                                    string sensorName = "Unknown";
                                    Match matchSensor = CpuSensorRegex.Match(msg);
                                    if (matchSensor.Success)
                                    {
                                        sensorName = matchSensor.Groups[1].Value.Trim();
                                    }

                                    TemperatureLogEntry item = new TemperatureLogEntry();
                                    item.Timestamp = entry.TimeGenerated;
                                    item.MaxTemperature = temp;
                                    item.MaxSensorName = sensorName;
                                    item.IsWarning = (entry.EntryType == EventLogEntryType.Warning || entry.InstanceId == EventIdWarning);

                                    // iGPU 温度の解析
                                    Match matchIGpu = IGpuTempRegex.Match(msg);
                                    if (matchIGpu.Success)
                                    {
                                        float igVal;
                                        if (float.TryParse(matchIGpu.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out igVal))
                                        {
                                            item.IGpuTemperature = igVal;
                                            Match matchIGpuSensor = IGpuSensorRegex.Match(msg);
                                            if (matchIGpuSensor.Success)
                                            {
                                                item.IGpuSensorName = matchIGpuSensor.Groups[1].Value.Trim();
                                            }
                                        }
                                    }

                                    // 各 dGPU 温度の解析
                                    MatchCollection dgpuMatches = DGpuTempRegex.Matches(msg);
                                    foreach (Match m in dgpuMatches)
                                    {
                                        string key = NormalizeDeviceId(m.Groups[1].Value);
                                        float dgVal;
                                        if (float.TryParse(m.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out dgVal))
                                        {
                                            item.DGpuTemperatures[key] = dgVal;
                                        }
                                    }

                                    MatchCollection dgpuSensorMatches = DGpuSensorRegex.Matches(msg);
                                    foreach (Match m in dgpuSensorMatches)
                                    {
                                        string key = NormalizeDeviceId(m.Groups[1].Value);
                                        item.DGpuSensorNames[key] = m.Groups[2].Value.Trim();
                                    }

                                    // 旧 GPU 温度のフォールバック
                                    Match matchLegacyGpu = LegacyGpuTempRegex.Match(msg);
                                    if (matchLegacyGpu.Success)
                                    {
                                        float lgVal;
                                        if (float.TryParse(matchLegacyGpu.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out lgVal))
                                        {
                                            item.GpuTemperature = lgVal;
                                            Match matchLgSensor = LegacyGpuSensorRegex.Match(msg);
                                            if (matchLgSensor.Success)
                                            {
                                                item.GpuSensorName = matchLgSensor.Groups[1].Value.Trim();
                                            }
                                            if (item.DGpuTemperatures.Count == 0)
                                            {
                                                item.DGpuTemperatures["dGPU:0"] = lgVal;
                                                item.DGpuSensorNames["dGPU:0"] = item.GpuSensorName ?? "GPU";
                                            }
                                        }
                                    }

                                    dict[entry.TimeGenerated] = item;
                                }
                            }
                        }
                        catch
                        {
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(string.Format("ReadEntriesFromLog({0}) failed: {1}", logName, ex.Message));
            }
        }

        private static string NormalizeDeviceId(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "dGPU:0";
            string s = raw.Trim();
            if (s.IndexOf(":") >= 0) return s;
            if (s.IndexOf("#") >= 0)
            {
                return s.Replace("#", ":");
            }
            if (s.Equals("dGPU", StringComparison.OrdinalIgnoreCase)) return "dGPU:0";
            return s;
        }
    }
}
