// --------------------------------------------------------------------------------------------------------------------
// <copyright file="HardwareMonitor.cs" company="CPU Thermo Watcher Contributors">
//   Copyright (c) 2026, CPU Thermo Watcher Contributors. All rights reserved.
//   Licensed under the 3-Clause BSD License.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Management;
using LibreHardwareMonitor.Hardware;

namespace CpuThermoWatcher
{
    public enum DeviceCategory
    {
        Cpu,
        IGpu,
        DGpu
    }

    public class ClockReading
    {
        public string CoreName { get; set; } // e.g. "Core #1", "Core #2"
        public float Value { get; set; }     // MHz

        public ClockReading(string coreName, float value)
        {
            CoreName = coreName;
            Value = value;
        }

        public override string ToString()
        {
            return string.Format("{0}: {1:F0}MHz", CoreName, Value);
        }
    }

    public class TemperatureReading
    {
        public string HardwareName { get; set; }
        public string SensorName { get; set; }
        public float Value { get; set; }
        public string SensorType { get; set; }
        public DeviceCategory Category { get; set; }
        public string DeviceId { get; set; }   // e.g. "CPU", "iGPU", "dGPU:0", "dGPU:1"
        public string DeviceName { get; set; } // e.g. "AMD Ryzen 9 7945HX", "AMD Radeon 610M", "NVIDIA GeForce RTX 4060 Laptop GPU"

        public bool IsGpu
        {
            get { return Category == DeviceCategory.IGpu || Category == DeviceCategory.DGpu; }
        }

        public TemperatureReading(
            string hardwareName,
            string sensorName,
            float value,
            string sensorType,
            DeviceCategory category = DeviceCategory.Cpu,
            string deviceId = null,
            string deviceName = null)
        {
            HardwareName = hardwareName;
            SensorName = sensorName;
            Value = value;
            SensorType = sensorType;
            Category = category;
            DeviceId = !string.IsNullOrEmpty(deviceId) ? deviceId : (category == DeviceCategory.Cpu ? "CPU" : (category == DeviceCategory.IGpu ? "iGPU" : "dGPU:0"));
            DeviceName = !string.IsNullOrEmpty(deviceName) ? deviceName : hardwareName;
        }

        public override string ToString()
        {
            return string.Format("[{0}] {1} - {2}: {3:F1}℃", DeviceId, DeviceName, SensorName, Value);
        }
    }

    public class HardwareDeviceSummary
    {
        public string DeviceId { get; set; }           // "CPU", "iGPU", "dGPU:0", "dGPU:1"
        public DeviceCategory Category { get; set; }   // Cpu, IGpu, DGpu
        public string DeviceName { get; set; }         // "AMD Ryzen 9 7945HX", "AMD Radeon 610M", "NVIDIA GeForce RTX 4060"
        public string DisplayName { get; set; }        // "CPU (AMD Ryzen 9 7945HX)", "dGPU #0 (RTX 4060)"
        public TemperatureReading MaxSensor { get; set; }

        public HardwareDeviceSummary(string deviceId, DeviceCategory category, string deviceName, string displayName, TemperatureReading maxSensor)
        {
            DeviceId = deviceId;
            Category = category;
            DeviceName = deviceName;
            DisplayName = displayName;
            MaxSensor = maxSensor;
        }
    }

    public class CpuTemperatureSnapshot
    {
        public List<TemperatureReading> Sensors { get; set; }
        public List<ClockReading> CpuClocks { get; set; }
        public List<HardwareDeviceSummary> Devices { get; set; }
        public TemperatureReading MaxCpuSensor { get; set; }
        public TemperatureReading MaxIGpuSensor { get; set; }
        public TemperatureReading MaxGpuSensor { get; set; } // 全GPUの中の最高値 (旧互換用)
        public DateTime Timestamp { get; set; }
        public bool Success { get; set; }
        public string Message { get; set; }

        public float MaxCpuClock
        {
            get
            {
                if (CpuClocks == null || CpuClocks.Count == 0) return 0f;
                float max = 0f;
                for (int i = 0; i < CpuClocks.Count; i++)
                {
                    if (CpuClocks[i].Value > max) max = CpuClocks[i].Value;
                }
                return max;
            }
        }

        public float AvgCpuClock
        {
            get
            {
                if (CpuClocks == null || CpuClocks.Count == 0) return 0f;
                float sum = 0f;
                for (int i = 0; i < CpuClocks.Count; i++)
                {
                    sum += CpuClocks[i].Value;
                }
                return sum / CpuClocks.Count;
            }
        }

        public CpuTemperatureSnapshot()
        {
            Sensors = new List<TemperatureReading>();
            CpuClocks = new List<ClockReading>();
            Devices = new List<HardwareDeviceSummary>();
            Timestamp = DateTime.Now;
            Success = false;
            Message = string.Empty;
        }

        public HardwareDeviceSummary GetDevice(string deviceId)
        {
            if (Devices == null) return null;
            for (int i = 0; i < Devices.Count; i++)
            {
                if (string.Equals(Devices[i].DeviceId, deviceId, StringComparison.OrdinalIgnoreCase))
                {
                    return Devices[i];
                }
            }
            return null;
        }

        public TemperatureReading GetMaxSensor(string deviceId)
        {
            HardwareDeviceSummary dev = GetDevice(deviceId);
            return dev != null ? dev.MaxSensor : null;
        }

        public bool IsAnyDeviceOverThreshold(Settings settings)
        {
            if (settings == null) return false;

            if (MaxCpuSensor != null && MaxCpuSensor.Value >= settings.WarningThresholdCelsius)
            {
                return true;
            }

            if (MaxIGpuSensor != null && MaxIGpuSensor.Value >= settings.WarningThresholdIGpuCelsius)
            {
                return true;
            }

            if (Devices != null)
            {
                for (int i = 0; i < Devices.Count; i++)
                {
                    HardwareDeviceSummary dev = Devices[i];
                    if (dev.Category == DeviceCategory.DGpu &&
                        dev.MaxSensor != null &&
                        dev.MaxSensor.Value >= settings.WarningThresholdDGpuCelsius)
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }

    public class HardwareMonitor : IDisposable
    {
        private Computer _computer;
        private List<PerformanceCounter> _clockCounters;
        private bool _countersInitialized;
        private bool _isDisposed;

        public HardwareMonitor()
        {
            try
            {
                _computer = new Computer();
                _computer.IsCpuEnabled = true;
                _computer.IsGpuEnabled = true; // GPU (NVIDIA / AMD / Intel) の温度取得を有効化
                _computer.Open();
            }
            catch (Exception ex)
            {
                _computer = null;
                Debug.WriteLine("Failed to initialize LibreHardwareMonitor: " + ex.Message);
            }
        }

        private void InitializeClockCounters()
        {
            if (_countersInitialized) return;
            _clockCounters = new List<PerformanceCounter>();

            try
            {
                PerformanceCounterCategory cat = new PerformanceCounterCategory("Processor Information");
                string[] instances = cat.GetInstanceNames();

                // 自然順ソート (0,0, 0,1, ... 0,15, 0,16 ...)
                Array.Sort(instances, NaturalStringComparer.Instance);

                foreach (string inst in instances)
                {
                    if (inst.IndexOf("Total", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    try
                    {
                        PerformanceCounter pc = new PerformanceCounter("Processor Information", "Processor Frequency", inst, true);
                        pc.NextValue(); // 初回サンプリング
                        _clockCounters.Add(pc);
                    }
                    catch
                    {
                    }
                }
                _countersInitialized = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("InitializeClockCounters failed: " + ex.Message);
            }
        }

        public static void ClassifyDevice(
            string rawHardwareName,
            string sensorLabel,
            out DeviceCategory category,
            out string deviceId,
            out string cleanDeviceName)
        {
            category = DeviceCategory.Cpu;
            deviceId = "CPU";
            cleanDeviceName = rawHardwareName ?? string.Empty;

            if (string.IsNullOrEmpty(rawHardwareName))
            {
                return;
            }

            bool isCpuWithRadeon = rawHardwareName.IndexOf("with Radeon Graphics", StringComparison.OrdinalIgnoreCase) >= 0 &&
                                   rawHardwareName.IndexOf("GPU", StringComparison.OrdinalIgnoreCase) < 0;

            // 1. iGPU (内蔵 GPU) 判定
            if (!isCpuWithRadeon && (
                rawHardwareName.IndexOf("iGPU", StringComparison.OrdinalIgnoreCase) >= 0 ||
                rawHardwareName.IndexOf("内蔵", StringComparison.OrdinalIgnoreCase) >= 0 ||
                (!string.IsNullOrEmpty(sensorLabel) && sensorLabel.IndexOf("iGPU", StringComparison.OrdinalIgnoreCase) >= 0) ||
                rawHardwareName.IndexOf("Radeon 610M", StringComparison.OrdinalIgnoreCase) >= 0 ||
                rawHardwareName.IndexOf("Radeon(TM) 610M", StringComparison.OrdinalIgnoreCase) >= 0 ||
                rawHardwareName.IndexOf("Radeon 780M", StringComparison.OrdinalIgnoreCase) >= 0 ||
                rawHardwareName.IndexOf("Radeon(TM) 780M", StringComparison.OrdinalIgnoreCase) >= 0 ||
                rawHardwareName.IndexOf("Radeon 680M", StringComparison.OrdinalIgnoreCase) >= 0 ||
                rawHardwareName.IndexOf("Radeon(TM) 680M", StringComparison.OrdinalIgnoreCase) >= 0 ||
                rawHardwareName.IndexOf("Radeon Graphics", StringComparison.OrdinalIgnoreCase) >= 0 ||
                rawHardwareName.IndexOf("Radeon(TM) Graphics", StringComparison.OrdinalIgnoreCase) >= 0 ||
                rawHardwareName.IndexOf("Intel(R) HD", StringComparison.OrdinalIgnoreCase) >= 0 ||
                rawHardwareName.IndexOf("Intel(R) UHD", StringComparison.OrdinalIgnoreCase) >= 0 ||
                rawHardwareName.IndexOf("Intel(R) Iris", StringComparison.OrdinalIgnoreCase) >= 0 ||
                rawHardwareName.IndexOf("Intel HD", StringComparison.OrdinalIgnoreCase) >= 0 ||
                rawHardwareName.IndexOf("Intel UHD", StringComparison.OrdinalIgnoreCase) >= 0 ||
                rawHardwareName.IndexOf("Intel Iris", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                category = DeviceCategory.IGpu;
                deviceId = "iGPU";
                cleanDeviceName = StripPrefix(rawHardwareName);
                return;
            }

            // 2. dGPU (専用 GPU) 判定
            if (rawHardwareName.IndexOf("dGPU", StringComparison.OrdinalIgnoreCase) >= 0 ||
                (!string.IsNullOrEmpty(sensorLabel) && sensorLabel.IndexOf("dGPU", StringComparison.OrdinalIgnoreCase) >= 0) ||
                rawHardwareName.IndexOf("GeForce", StringComparison.OrdinalIgnoreCase) >= 0 ||
                rawHardwareName.IndexOf("RTX", StringComparison.OrdinalIgnoreCase) >= 0 ||
                rawHardwareName.IndexOf("GTX", StringComparison.OrdinalIgnoreCase) >= 0 ||
                rawHardwareName.IndexOf("Quadro", StringComparison.OrdinalIgnoreCase) >= 0 ||
                rawHardwareName.IndexOf("Radeon RX", StringComparison.OrdinalIgnoreCase) >= 0 ||
                rawHardwareName.IndexOf("Radeon Pro", StringComparison.OrdinalIgnoreCase) >= 0 ||
                rawHardwareName.IndexOf("Arc A", StringComparison.OrdinalIgnoreCase) >= 0 ||
                rawHardwareName.IndexOf("Arc B", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                category = DeviceCategory.DGpu;
                int dgpuIdx = ExtractIndex(rawHardwareName);
                deviceId = "dGPU:" + dgpuIdx;
                cleanDeviceName = StripPrefix(rawHardwareName);
                return;
            }

            // 3. 一般的な GPU 表記 (例: "GPU Core")
            if (rawHardwareName.IndexOf("GPU", StringComparison.OrdinalIgnoreCase) >= 0 ||
                (!string.IsNullOrEmpty(sensorLabel) && sensorLabel.IndexOf("GPU", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                category = DeviceCategory.DGpu;
                deviceId = "dGPU:0";
                cleanDeviceName = StripPrefix(rawHardwareName);
                return;
            }

            // 4. デフォルト: CPU
            category = DeviceCategory.Cpu;
            deviceId = "CPU";
            cleanDeviceName = rawHardwareName;
        }

        private static int ExtractIndex(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            int hashPos = text.IndexOf("#");
            if (hashPos >= 0 && hashPos + 1 < text.Length)
            {
                char c = text[hashPos + 1];
                if (char.IsDigit(c))
                {
                    return c - '0';
                }
            }
            return 0;
        }

        private static string StripPrefix(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            int colonPos = text.IndexOf(":");
            if (colonPos >= 0 && colonPos + 1 < text.Length)
            {
                string afterColon = text.Substring(colonPos + 1).Trim();
                if (!string.IsNullOrEmpty(afterColon)) return afterColon;
            }
            return text.Trim();
        }

        public CpuTemperatureSnapshot ReadCpuTemperatures()
        {
            CpuTemperatureSnapshot snapshot = new CpuTemperatureSnapshot();

            // 1. LibreHardwareMonitor によるダイレクト計測 (CPU & GPU 温度およびクロック)
            string detectedCpuName = string.Empty;
            int dgpuCounter = 0;
            List<ClockReading> lhmClocks = new List<ClockReading>();

            if (_computer != null)
            {
                try
                {
                    foreach (IHardware hardware in _computer.Hardware)
                    {
                        if (hardware.HardwareType == HardwareType.Cpu)
                        {
                            hardware.Update();
                            detectedCpuName = hardware.Name;

                            CollectCpuSensors(hardware, hardware.Name, snapshot.Sensors, lhmClocks);

                            foreach (IHardware subHardware in hardware.SubHardware)
                            {
                                subHardware.Update();
                                CollectCpuSensors(subHardware, hardware.Name, snapshot.Sensors, lhmClocks);
                            }
                        }
                        else if (hardware.HardwareType == HardwareType.GpuNvidia ||
                                 hardware.HardwareType == HardwareType.GpuAmd ||
                                 hardware.HardwareType == HardwareType.GpuIntel)
                        {
                            hardware.Update();

                            DeviceCategory cat;
                            string devId;
                            string devName;
                            ClassifyDevice(hardware.Name, string.Empty, out cat, out devId, out devName);
                            if (cat == DeviceCategory.DGpu && devId == "dGPU:0" && dgpuCounter > 0)
                            {
                                devId = "dGPU:" + dgpuCounter;
                            }
                            if (cat == DeviceCategory.DGpu) dgpuCounter++;

                            CollectGpuSensors(hardware, cat, devId, devName, snapshot.Sensors);

                            foreach (IHardware subHardware in hardware.SubHardware)
                            {
                                subHardware.Update();
                                CollectGpuSensors(subHardware, cat, devId, devName, snapshot.Sensors);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    snapshot.Message = "LHM read error: " + ex.Message;
                }
            }

            // 2. 外部ツール共有メモリからの取得 (HWiNFO64, Core Temp, AIDA64)
            MergeSensors(snapshot.Sensors, HWiNFOReader.TryReadHWiNFO(), detectedCpuName);
            MergeSensors(snapshot.Sensors, CoreTempReader.TryReadCoreTemp(), detectedCpuName);
            MergeSensors(snapshot.Sensors, AIDA64Reader.TryReadAIDA64(), detectedCpuName);

            // 3. CPU コアごとのクロック取得 (LHM または Windows パフォーマンスカウンター)
            if (lhmClocks.Count > 0)
            {
                lhmClocks.Sort(delegate(ClockReading a, ClockReading b)
                {
                    return NaturalStringComparer.CompareNatural(a.CoreName, b.CoreName);
                });
                snapshot.CpuClocks = lhmClocks;
            }
            else
            {
                snapshot.CpuClocks = ReadPerformanceCounterClocks();
            }

            // 4. フォールバック: WMI (MSAcpi_ThermalZoneTemperature)
            if (snapshot.Sensors.Count == 0)
            {
                try
                {
                    using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(@"root\wmi", "SELECT * FROM MSAcpi_ThermalZoneTemperature"))
                    using (ManagementObjectCollection results = searcher.Get())
                    {
                        foreach (ManagementObject obj in results)
                        {
                            using (obj)
                            {
                                object val = obj["CurrentTemperature"];
                                if (val != null)
                                {
                                    double kelvinTenths = Convert.ToDouble(val);
                                    float celsius = (float)((kelvinTenths / 10.0) - 273.15);
                                    string instanceName = Convert.ToString(obj["InstanceName"]);
                                    if (string.IsNullOrEmpty(instanceName)) instanceName = "ACPI Thermal Zone";

                                    snapshot.Sensors.Add(new TemperatureReading(
                                        "ACPI WMI",
                                        instanceName,
                                        celsius,
                                        "ThermalZone",
                                        DeviceCategory.Cpu,
                                        "CPU",
                                        "ACPI WMI"
                                    ));
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    snapshot.Message = "WMI fallback error: " + ex.Message;
                }
            }

            // 5. デバイス一覧 (Devices) の集約と最高温度の算出
            if (snapshot.Sensors.Count > 0)
            {
                BuildDeviceSummaries(snapshot);
                snapshot.Success = (snapshot.MaxCpuSensor != null || snapshot.Devices.Count > 0);
                return snapshot;
            }

            snapshot.Success = false;
            if (string.IsNullOrEmpty(snapshot.Message))
            {
                snapshot.Message = "CPU/GPU温度センサーが見つかりませんでした。管理者権限で実行されているかご確認ください。";
            }

            return snapshot;
        }

        private static void CollectCpuSensors(
            IHardware sourceHardware,
            string cpuDeviceName,
            List<TemperatureReading> sensors,
            List<ClockReading> clocks)
        {
            foreach (ISensor sensor in sourceHardware.Sensors)
            {
                if (sensor.SensorType == SensorType.Temperature && sensor.Value.HasValue && sensor.Value.Value > 0.0f && sensor.Value.Value < 150.0f)
                {
                    AddUniqueSensor(sensors, new TemperatureReading(
                        sourceHardware.Name,
                        sensor.Name,
                        sensor.Value.Value,
                        sensor.SensorType.ToString(),
                        DeviceCategory.Cpu,
                        "CPU",
                        cpuDeviceName
                    ));
                }
                else if (sensor.SensorType == SensorType.Clock && sensor.Value.HasValue && IsCpuCoreClockSensor(sensor.Name))
                {
                    float val = sensor.Value.Value;
                    if (!float.IsNaN(val) && val > 100.0f && val < 10000.0f)
                    {
                        clocks.Add(new ClockReading(sensor.Name, val));
                    }
                }
            }
        }

        private static bool IsCpuCoreClockSensor(string sensorName)
        {
            if (string.IsNullOrEmpty(sensorName)) return false;
            if (sensorName.IndexOf("Bus", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (sensorName.IndexOf("FCLK", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (sensorName.IndexOf("UCLK", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (sensorName.IndexOf("MCLK", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (sensorName.IndexOf("Memory", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            return sensorName.IndexOf("Core", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   sensorName.IndexOf("#", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void CollectGpuSensors(
            IHardware sourceHardware,
            DeviceCategory category,
            string deviceId,
            string deviceName,
            List<TemperatureReading> sensors)
        {
            foreach (ISensor sensor in sourceHardware.Sensors)
            {
                if (sensor.SensorType == SensorType.Temperature && sensor.Value.HasValue && sensor.Value.Value > 0.0f && sensor.Value.Value < 150.0f)
                {
                    AddUniqueSensor(sensors, new TemperatureReading(
                        deviceName,
                        sensor.Name,
                        sensor.Value.Value,
                        "GPU",
                        category,
                        deviceId,
                        deviceName
                    ));
                }
            }
        }

        private List<ClockReading> ReadPerformanceCounterClocks()
        {
            List<ClockReading> list = new List<ClockReading>();
            try
            {
                InitializeClockCounters();
                if (_clockCounters != null && _clockCounters.Count > 0)
                {
                    for (int i = 0; i < _clockCounters.Count; i++)
                    {
                        try
                        {
                            float freq = _clockCounters[i].NextValue();
                            if (freq > 0.0f)
                            {
                                string coreLabel = string.Format("Core #{0}", i + 1);
                                list.Add(new ClockReading(coreLabel, freq));
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
                System.Diagnostics.Debug.WriteLine("ReadPerformanceCounterClocks failed: " + ex.Message);
            }
            return list;
        }

        private static void BuildDeviceSummaries(CpuTemperatureSnapshot snapshot)
        {
            if (snapshot == null || snapshot.Sensors == null) return;

            Dictionary<string, List<TemperatureReading>> map = new Dictionary<string, List<TemperatureReading>>();
            for (int i = 0; i < snapshot.Sensors.Count; i++)
            {
                TemperatureReading r = snapshot.Sensors[i];
                string id = r.DeviceId ?? "CPU";
                if (!map.ContainsKey(id))
                {
                    map[id] = new List<TemperatureReading>();
                }
                map[id].Add(r);
            }

            // 順序付け: CPU -> iGPU -> dGPU:0 -> dGPU:1 ...
            List<string> orderedIds = new List<string>();
            if (map.ContainsKey("CPU")) orderedIds.Add("CPU");
            if (map.ContainsKey("iGPU")) orderedIds.Add("iGPU");

            List<string> dgpuKeys = new List<string>();
            foreach (string k in map.Keys)
            {
                if (k.StartsWith("dGPU", StringComparison.OrdinalIgnoreCase))
                {
                    dgpuKeys.Add(k);
                }
            }
            dgpuKeys.Sort(StringComparer.OrdinalIgnoreCase);
            orderedIds.AddRange(dgpuKeys);

            // 残りの未知キー
            foreach (string k in map.Keys)
            {
                if (!orderedIds.Contains(k)) orderedIds.Add(k);
            }

            snapshot.Devices.Clear();
            for (int i = 0; i < orderedIds.Count; i++)
            {
                string id = orderedIds[i];
                List<TemperatureReading> list = map[id];
                TemperatureReading max = null;
                string devName = id;
                DeviceCategory cat = DeviceCategory.Cpu;

                for (int j = 0; j < list.Count; j++)
                {
                    TemperatureReading r = list[j];
                    if (max == null || r.Value > max.Value)
                    {
                        max = r;
                    }
                    if (!string.IsNullOrEmpty(r.DeviceName))
                    {
                        devName = r.DeviceName;
                    }
                    cat = r.Category;
                }

                string displayName;
                if (cat == DeviceCategory.Cpu)
                {
                    displayName = "CPU (" + devName + ")";
                }
                else if (cat == DeviceCategory.IGpu)
                {
                    displayName = "iGPU (" + devName + ")";
                }
                else
                {
                    displayName = UiHelper.FormatDeviceLabel(id) + " (" + devName + ")";
                }

                HardwareDeviceSummary summary = new HardwareDeviceSummary(id, cat, devName, displayName, max);
                snapshot.Devices.Add(summary);
            }

            snapshot.MaxCpuSensor = snapshot.GetMaxSensor("CPU");
            snapshot.MaxIGpuSensor = snapshot.GetMaxSensor("iGPU");
            snapshot.MaxGpuSensor = FindMaxGpuSensor(snapshot.Sensors);
        }

        private static TemperatureReading FindMaxGpuSensor(List<TemperatureReading> sensors)
        {
            if (sensors == null || sensors.Count == 0) return null;
            TemperatureReading max = null;
            for (int i = 0; i < sensors.Count; i++)
            {
                if (!sensors[i].IsGpu) continue;
                if (max == null || sensors[i].Value > max.Value)
                {
                    max = sensors[i];
                }
            }
            return max;
        }

        private static void MergeSensors(List<TemperatureReading> destination, List<TemperatureReading> source, string defaultCpuName)
        {
            if (source == null || source.Count == 0 || destination == null) return;
            for (int i = 0; i < source.Count; i++)
            {
                TemperatureReading r = source[i];
                if (r.Category == DeviceCategory.Cpu && !string.IsNullOrEmpty(defaultCpuName))
                {
                    r.HardwareName = defaultCpuName;
                    r.DeviceName = defaultCpuName;
                }
                AddUniqueSensor(destination, r);
            }
        }

        private static void AddUniqueSensor(List<TemperatureReading> list, TemperatureReading reading)
        {
            if (reading == null || list == null) return;
            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i].DeviceId, reading.DeviceId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(list[i].SensorName, reading.SensorName, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }
            list.Add(reading);
        }

        public void Dispose()
        {
            if (!_isDisposed)
            {
                if (_computer != null)
                {
                    try
                    {
                        _computer.Close();
                    }
                    catch
                    {
                    }
                    _computer = null;
                }

                if (_clockCounters != null)
                {
                    for (int i = 0; i < _clockCounters.Count; i++)
                    {
                        try { _clockCounters[i].Dispose(); } catch { }
                    }
                    _clockCounters.Clear();
                    _clockCounters = null;
                }

                _isDisposed = true;
            }
        }
    }
}
