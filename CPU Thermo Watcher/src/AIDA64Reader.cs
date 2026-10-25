// --------------------------------------------------------------------------------------------------------------------
// <copyright file="AIDA64Reader.cs" company="CPU Thermo Watcher Contributors">
//   Copyright (c) 2026, CPU Thermo Watcher Contributors. All rights reserved.
//   Licensed under the 3-Clause BSD License.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO.MemoryMappedFiles;
using System.Text;
using System.Xml.Linq;

namespace CpuThermoWatcher
{
    public static class AIDA64Reader
    {
        private const string SharedMemoryName = "AIDA64_SensorValues";

        public static List<TemperatureReading> TryReadAIDA64()
        {
            try
            {
                using (MemoryMappedFile mmf = MemoryMappedFile.OpenExisting(SharedMemoryName, MemoryMappedFileRights.Read))
                {
                    using (MemoryMappedViewAccessor accessor = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read))
                    {
                        if (accessor.Capacity <= 0) return null;

                        int bufferSize = (int)Math.Min(accessor.Capacity, 65536);
                        byte[] buffer = new byte[bufferSize];
                        accessor.ReadArray(0, buffer, 0, buffer.Length);

                        int length = Array.IndexOf(buffer, (byte)0);
                        if (length <= 0) length = buffer.Length;

                        string rawXml = Encoding.UTF8.GetString(buffer, 0, length).Trim();
                        if (string.IsNullOrEmpty(rawXml)) return null;

                        // AIDA64 のフラグメント XML をラップしてパース
                        string wrappedXml = string.Format("<AIDA64>{0}</AIDA64>", rawXml);
                        XDocument doc = XDocument.Parse(wrappedXml);
                        if (doc.Root == null) return null;

                        List<TemperatureReading> readings = new List<TemperatureReading>();

                        foreach (XElement elem in doc.Root.Elements())
                        {
                            XElement idElem = elem.Element("id");
                            XElement labelElem = elem.Element("label");
                            XElement valElem = elem.Element("value");

                            if (labelElem == null || valElem == null) continue;

                            string id = idElem != null ? idElem.Value : string.Empty;
                            string label = labelElem.Value;
                            string valStr = valElem.Value;

                            bool isGpu = id.IndexOf("GPU", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                         label.IndexOf("GPU", StringComparison.OrdinalIgnoreCase) >= 0;

                            // 温度センサー (ID が T または Label が CPU/Core/CCD/GPU 関連)
                            if (id.StartsWith("T", StringComparison.OrdinalIgnoreCase) ||
                                label.IndexOf("CPU", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                label.IndexOf("Core", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                label.IndexOf("CCD", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                isGpu)
                            {
                                float temp;
                                if (float.TryParse(valStr, NumberStyles.Float, CultureInfo.InvariantCulture, out temp))
                                {
                                    if (temp > 10.0f && temp < 135.0f)
                                    {
                                        DeviceCategory devCat;
                                        string devId;
                                        string devName;
                                        HardwareMonitor.ClassifyDevice(isGpu ? "GPU" : "AIDA64", label, out devCat, out devId, out devName);

                                        readings.Add(new TemperatureReading(
                                            devName,
                                            label,
                                            temp,
                                            devCat == DeviceCategory.Cpu ? "Temperature" : "GPU",
                                            devCat,
                                            devId,
                                            devName
                                        ));
                                    }
                                }
                            }
                        }

                        if (readings.Count > 0)
                        {
                            return readings;
                        }
                    }
                }
            }
            catch
            {
                // AIDA64 未起動または共有メモリ無効
            }

            return null;
        }
    }
}
