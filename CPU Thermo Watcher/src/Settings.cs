// --------------------------------------------------------------------------------------------------------------------
// <copyright file="Settings.cs" company="CPU Thermo Watcher Contributors">
//   Copyright (c) 2026, CPU Thermo Watcher Contributors. All rights reserved.
//   Licensed under the 3-Clause BSD License.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CpuThermoWatcher
{
    public class Settings
    {
        private const string FileName = "settings.json";

        public int UpdateIntervalMs { get; set; }
        public float WarningThresholdCelsius { get; set; }     // CPU 警告閾値
        public float WarningThresholdIGpuCelsius { get; set; } // iGPU 警告閾値
        public float WarningThresholdDGpuCelsius { get; set; } // dGPU 警告閾値

        public bool ShowCpuInTray { get; set; }
        public bool ShowIGpuInTray { get; set; }
        public bool ShowDGpuInTray { get; set; }

        public List<string> EnabledTrayDeviceIds { get; set; } // 表示対象デバイスIDのリスト ("CPU", "iGPU", "dGPU:0" など)

        public bool EnableEventLogging { get; set; }
        public int EventLogIntervalSeconds { get; set; }
        public string TextColorMode { get; set; } // "Auto", "White", "Black"

        public Settings()
        {
            UpdateIntervalMs = 2000;
            WarningThresholdCelsius = 80.0f;
            WarningThresholdIGpuCelsius = 85.0f;
            WarningThresholdDGpuCelsius = 85.0f;
            ShowCpuInTray = true;
            ShowIGpuInTray = false;
            ShowDGpuInTray = false;
            EnabledTrayDeviceIds = new List<string>() { "CPU" };
            EnableEventLogging = true;
            EventLogIntervalSeconds = 10;
            TextColorMode = "Auto";
        }

        public bool IsDeviceInTray(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId)) return false;
            if (EnabledTrayDeviceIds != null && EnabledTrayDeviceIds.Count > 0)
            {
                for (int i = 0; i < EnabledTrayDeviceIds.Count; i++)
                {
                    if (string.Equals(EnabledTrayDeviceIds[i], deviceId, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                return false;
            }
            // フォールバック
            if (string.Equals(deviceId, "CPU", StringComparison.OrdinalIgnoreCase)) return ShowCpuInTray;
            if (string.Equals(deviceId, "iGPU", StringComparison.OrdinalIgnoreCase)) return ShowIGpuInTray;
            if (deviceId.StartsWith("dGPU", StringComparison.OrdinalIgnoreCase)) return ShowDGpuInTray;
            return false;
        }

        public void SetDeviceInTray(string deviceId, bool enabled)
        {
            if (string.IsNullOrEmpty(deviceId)) return;
            if (EnabledTrayDeviceIds == null) EnabledTrayDeviceIds = new List<string>();

            int foundIdx = -1;
            for (int i = 0; i < EnabledTrayDeviceIds.Count; i++)
            {
                if (string.Equals(EnabledTrayDeviceIds[i], deviceId, StringComparison.OrdinalIgnoreCase))
                {
                    foundIdx = i;
                    break;
                }
            }

            if (enabled && foundIdx < 0)
            {
                EnabledTrayDeviceIds.Add(deviceId);
            }
            else if (!enabled && foundIdx >= 0)
            {
                EnabledTrayDeviceIds.RemoveAt(foundIdx);
            }

            // 旧互換フラグの同期
            if (string.Equals(deviceId, "CPU", StringComparison.OrdinalIgnoreCase)) ShowCpuInTray = enabled;
            if (string.Equals(deviceId, "iGPU", StringComparison.OrdinalIgnoreCase)) ShowIGpuInTray = enabled;
            if (deviceId.StartsWith("dGPU", StringComparison.OrdinalIgnoreCase)) ShowDGpuInTray = enabled;
        }

        public float GetWarningThreshold(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId)) return WarningThresholdCelsius;
            if (string.Equals(deviceId, "CPU", StringComparison.OrdinalIgnoreCase)) return WarningThresholdCelsius;
            if (string.Equals(deviceId, "iGPU", StringComparison.OrdinalIgnoreCase)) return WarningThresholdIGpuCelsius;
            return WarningThresholdDGpuCelsius;
        }

        public static string GetConfigPath()
        {
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            return Path.Combine(appDir, FileName);
        }

        public static Settings Load()
        {
            Settings settings = new Settings();
            string path = GetConfigPath();

            if (!File.Exists(path))
            {
                settings.Save();
                return settings;
            }

            try
            {
                string text = File.ReadAllText(path, Encoding.UTF8);
                string[] lines = text.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

                foreach (string rawLine in lines)
                {
                    string line = rawLine.Trim();
                    if (string.IsNullOrEmpty(line) || line.StartsWith("{") || line.StartsWith("}"))
                    {
                        continue;
                    }

                    int colonIndex = line.IndexOf(':');
                    if (colonIndex < 0) continue;

                    string key = line.Substring(0, colonIndex).Trim().Trim('"', ',');
                    string val = line.Substring(colonIndex + 1).Trim().Trim('"', ',');

                    if (key.Equals("UpdateIntervalMs", StringComparison.OrdinalIgnoreCase))
                    {
                        int iv;
                        if (int.TryParse(val, out iv) && iv >= 500) settings.UpdateIntervalMs = iv;
                    }
                    else if (key.Equals("WarningThresholdCelsius", StringComparison.OrdinalIgnoreCase))
                    {
                        float fv;
                        if (float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out fv))
                        {
                            settings.WarningThresholdCelsius = fv;
                        }
                    }
                    else if (key.Equals("WarningThresholdIGpuCelsius", StringComparison.OrdinalIgnoreCase))
                    {
                        float fv;
                        if (float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out fv))
                        {
                            settings.WarningThresholdIGpuCelsius = fv;
                        }
                    }
                    else if (key.Equals("WarningThresholdDGpuCelsius", StringComparison.OrdinalIgnoreCase) ||
                             key.Equals("WarningThresholdGpuCelsius", StringComparison.OrdinalIgnoreCase))
                    {
                        float fv;
                        if (float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out fv))
                        {
                            settings.WarningThresholdDGpuCelsius = fv;
                        }
                    }
                    else if (key.Equals("ShowCpuInTray", StringComparison.OrdinalIgnoreCase))
                    {
                        bool bv;
                        if (bool.TryParse(val, out bv)) settings.ShowCpuInTray = bv;
                    }
                    else if (key.Equals("ShowIGpuInTray", StringComparison.OrdinalIgnoreCase))
                    {
                        bool bv;
                        if (bool.TryParse(val, out bv)) settings.ShowIGpuInTray = bv;
                    }
                    else if (key.Equals("ShowDGpuInTray", StringComparison.OrdinalIgnoreCase) ||
                             key.Equals("ShowGpuInTray", StringComparison.OrdinalIgnoreCase))
                    {
                        bool bv;
                        if (bool.TryParse(val, out bv)) settings.ShowDGpuInTray = bv;
                    }
                    else if (key.Equals("EnabledTrayDevices", StringComparison.OrdinalIgnoreCase))
                    {
                        string[] parts = val.Split(new char[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                        settings.EnabledTrayDeviceIds = new List<string>();
                        for (int i = 0; i < parts.Length; i++)
                        {
                            string item = parts[i].Trim();
                            if (!string.IsNullOrEmpty(item)) settings.EnabledTrayDeviceIds.Add(item);
                        }
                    }
                    else if (key.Equals("EnableEventLogging", StringComparison.OrdinalIgnoreCase))
                    {
                        bool bv;
                        if (bool.TryParse(val, out bv)) settings.EnableEventLogging = bv;
                    }
                    else if (key.Equals("EventLogIntervalSeconds", StringComparison.OrdinalIgnoreCase))
                    {
                        int ev;
                        if (int.TryParse(val, out ev) && ev >= 5) settings.EventLogIntervalSeconds = ev;
                    }
                    else if (key.Equals("TextColorMode", StringComparison.OrdinalIgnoreCase))
                    {
                        settings.TextColorMode = val;
                    }
                    else if (key.Equals("TextColorLight", StringComparison.OrdinalIgnoreCase))
                    {
                        bool tv;
                        if (bool.TryParse(val, out tv))
                        {
                            settings.TextColorMode = tv ? "White" : "Black";
                        }
                    }
                }
            }
            catch
            {
                // エラー時はデフォルト値を維持
            }

            // 同期処理
            if (settings.EnabledTrayDeviceIds == null || settings.EnabledTrayDeviceIds.Count == 0)
            {
                settings.EnabledTrayDeviceIds = new List<string>();
                if (settings.ShowCpuInTray) settings.EnabledTrayDeviceIds.Add("CPU");
                if (settings.ShowIGpuInTray) settings.EnabledTrayDeviceIds.Add("iGPU");
                if (settings.ShowDGpuInTray) settings.EnabledTrayDeviceIds.Add("dGPU:0");
            }
            if (settings.EnabledTrayDeviceIds.Count == 0)
            {
                settings.EnabledTrayDeviceIds.Add("CPU");
                settings.ShowCpuInTray = true;
            }

            return settings;
        }

        public void Save()
        {
            try
            {
                string path = GetConfigPath();
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("{");
                sb.AppendLine(string.Format("  \"UpdateIntervalMs\": {0},", UpdateIntervalMs));
                sb.AppendLine(string.Format(System.Globalization.CultureInfo.InvariantCulture, "  \"WarningThresholdCelsius\": {0:F1},", WarningThresholdCelsius));
                sb.AppendLine(string.Format(System.Globalization.CultureInfo.InvariantCulture, "  \"WarningThresholdIGpuCelsius\": {0:F1},", WarningThresholdIGpuCelsius));
                sb.AppendLine(string.Format(System.Globalization.CultureInfo.InvariantCulture, "  \"WarningThresholdDGpuCelsius\": {0:F1},", WarningThresholdDGpuCelsius));
                sb.AppendLine(string.Format("  \"ShowCpuInTray\": {0},", ShowCpuInTray ? "true" : "false"));
                sb.AppendLine(string.Format("  \"ShowIGpuInTray\": {0},", ShowIGpuInTray ? "true" : "false"));
                sb.AppendLine(string.Format("  \"ShowDGpuInTray\": {0},", ShowDGpuInTray ? "true" : "false"));

                string devicesJoined = (EnabledTrayDeviceIds != null) ? string.Join(",", EnabledTrayDeviceIds.ToArray()) : "CPU";
                sb.AppendLine(string.Format("  \"EnabledTrayDevices\": \"{0}\",", devicesJoined));

                sb.AppendLine(string.Format("  \"EnableEventLogging\": {0},", EnableEventLogging ? "true" : "false"));
                sb.AppendLine(string.Format("  \"EventLogIntervalSeconds\": {0},", EventLogIntervalSeconds));
                sb.AppendLine(string.Format("  \"TextColorMode\": \"{0}\"", TextColorMode ?? "Auto"));
                sb.AppendLine("}");

                File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            }
            catch
            {
            }
        }
    }
}
