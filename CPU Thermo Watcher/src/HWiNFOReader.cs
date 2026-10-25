// --------------------------------------------------------------------------------------------------------------------
// <copyright file="HWiNFOReader.cs" company="CPU Thermo Watcher Contributors">
//   Copyright (c) 2026, CPU Thermo Watcher Contributors. All rights reserved.
//   Licensed under the 3-Clause BSD License.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;

namespace CpuThermoWatcher
{
    public static class HWiNFOReader
    {
        private static readonly string[] SharedMemoryNames = new string[] {
            @"Global\HWiNFO_SENS_SM2",
            @"HWiNFO_SENS_SM2"
        };

        private static readonly string[] MutexNames = new string[] {
            @"Global\HWiNFO_SM2_MUTEX",
            @"HWiNFO_SM2_MUTEX"
        };

        private const uint HWiNFO_MAGIC = 0x48576953;     // 'HWiS'
        private const uint HWiNFO_MAGIC_REV = 0x53695748; // 'SiWH'
        private const uint SENSOR_TYPE_TEMPERATURE = 1;

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct HWiNFOHeader
        {
            public uint Magic;
            public uint Version;
            public uint Version2;
            public long LastUpdate;

            public uint SensorSectionOffset;
            public uint SensorElementSize;
            public uint SensorElementCount;

            public uint EntrySectionOffset;
            public uint EntryElementSize;
            public uint EntryElementCount;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Ansi)]
        private struct HWiNFOSensor
        {
            public uint Id;
            public uint Instance;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string NameOriginal;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string NameUser;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Ansi)]
        private struct HWiNFOEntry
        {
            public uint Type;
            public uint SensorIndex;
            public uint Id;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string NameOriginal;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string NameUser;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)]
            public string Unit;
            public double Value;
            public double ValueMin;
            public double ValueMax;
            public double ValueAvg;
        }

        public static List<TemperatureReading> TryReadHWiNFO()
        {
            // 1. 共有メモリからの取得を試行
            List<TemperatureReading> list = TryReadSharedMemory();
            if (list != null && list.Count > 0)
            {
                return list;
            }

            // 2. フォールバック: HWiNFO Gadget (レジストリ VSB) からの取得
            return TryReadHWiNFOFromRegistry();
        }

        private static List<TemperatureReading> TryReadSharedMemory()
        {
            foreach (string memName in SharedMemoryNames)
            {
                Mutex mutex = null;
                bool mutexAcquired = false;

                try
                {
                    // 排他ミューテックスの取得試行（最大50ms）
                    foreach (string mutName in MutexNames)
                    {
                        try
                        {
                            if (Mutex.TryOpenExisting(mutName, out mutex))
                            {
                                mutexAcquired = mutex.WaitOne(50);
                                if (mutexAcquired) break;
                            }
                        }
                        catch
                        {
                            mutex = null;
                            mutexAcquired = false;
                        }
                    }

                    using (MemoryMappedFile mmf = MemoryMappedFile.OpenExisting(memName, MemoryMappedFileRights.Read))
                    {
                        using (MemoryMappedViewAccessor accessor = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read))
                        {
                            int headerSize = Marshal.SizeOf(typeof(HWiNFOHeader));
                            if (accessor.Capacity < headerSize) continue;

                            HWiNFOHeader header = ReadStruct<HWiNFOHeader>(accessor, 0);

                            if (header.Magic != HWiNFO_MAGIC && header.Magic != HWiNFO_MAGIC_REV)
                            {
                                continue;
                            }

                            if (header.EntryElementCount == 0 || header.SensorElementCount == 0)
                            {
                                continue;
                            }

                            // センサーカテゴリ名（親グループ名）マップの構築
                            Dictionary<uint, string> sensorCategories = new Dictionary<uint, string>();
                            int sensorStructSize = Marshal.SizeOf(typeof(HWiNFOSensor));
                            long sensorOffset = header.SensorSectionOffset;

                            for (uint i = 0; i < header.SensorElementCount; i++)
                            {
                                long pos = sensorOffset + (i * header.SensorElementSize);
                                if (pos + sensorStructSize > accessor.Capacity) break;

                                HWiNFOSensor sensor = ReadStruct<HWiNFOSensor>(accessor, pos);
                                string name = !string.IsNullOrEmpty(sensor.NameUser) ? sensor.NameUser : sensor.NameOriginal;
                                sensorCategories[i] = name;
                            }

                            List<TemperatureReading> readings = new List<TemperatureReading>();
                            int entryStructSize = Marshal.SizeOf(typeof(HWiNFOEntry));
                            long entryOffset = header.EntrySectionOffset;

                            for (uint i = 0; i < header.EntryElementCount; i++)
                            {
                                long pos = entryOffset + (i * header.EntryElementSize);
                                if (pos + entryStructSize > accessor.Capacity) break;

                                HWiNFOEntry entry = ReadStruct<HWiNFOEntry>(accessor, pos);

                                // 温度センサーのみを対象 (HWiNFO SDK: Type == 1 が温度)
                                if (entry.Type != SENSOR_TYPE_TEMPERATURE) continue;

                                string category = sensorCategories.ContainsKey(entry.SensorIndex) ? sensorCategories[entry.SensorIndex] : "CPU";
                                string label = !string.IsNullOrEmpty(entry.NameUser) ? entry.NameUser : entry.NameOriginal;

                                bool isGpu;
                                if (IsTemperatureSensor(category, label, entry.Unit, out isGpu))
                                {
                                    if (entry.Value > 10.0 && entry.Value < 135.0)
                                    {
                                        DeviceCategory devCat;
                                        string devId;
                                        string devName;
                                        HardwareMonitor.ClassifyDevice(category, label, out devCat, out devId, out devName);

                                        readings.Add(new TemperatureReading(
                                            devName,
                                            label,
                                            (float)entry.Value,
                                            devCat == DeviceCategory.Cpu ? "Temperature" : "GPU",
                                            devCat,
                                            devId,
                                            devName
                                        ));
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
                    // 次の名前を試行
                }
                finally
                {
                    if (mutex != null)
                    {
                        if (mutexAcquired)
                        {
                            try { mutex.ReleaseMutex(); } catch { }
                        }
                        mutex.Dispose();
                    }
                }
            }

            return null;
        }

        private static List<TemperatureReading> TryReadHWiNFOFromRegistry()
        {
            try
            {
                using (RegistryKey vsbKey = Registry.CurrentUser.OpenSubKey(@"Software\HWiNFO64\VSB"))
                {
                    if (vsbKey == null) return null;

                    List<TemperatureReading> readings = new List<TemperatureReading>();
                    int index = 0;
                    while (true)
                    {
                        object labelObj = vsbKey.GetValue("Label" + index);
                        if (labelObj == null) break;

                        string label = labelObj.ToString();
                        string sensor = Convert.ToString(vsbKey.GetValue("Sensor" + index));
                        string unit = Convert.ToString(vsbKey.GetValue("Unit" + index));
                        object valObj = vsbKey.GetValue("ValueRaw" + index);
                        if (valObj == null) valObj = vsbKey.GetValue("Value" + index);

                        if (!string.IsNullOrEmpty(unit) && unit.IndexOf("%") >= 0) { index++; continue; }
                        if (label.IndexOf("使用率", StringComparison.OrdinalIgnoreCase) >= 0) { index++; continue; }
                        if (label.IndexOf("在留率", StringComparison.OrdinalIgnoreCase) >= 0) { index++; continue; }

                        if (valObj != null)
                        {
                            float tempVal;
                            if (float.TryParse(valObj.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out tempVal))
                            {
                                if (tempVal > 10.0f && tempVal < 135.0f)
                                {
                                    bool isGpu;
                                    if (IsTemperatureSensor(sensor, label, unit, out isGpu))
                                    {
                                        DeviceCategory devCat;
                                        string devId;
                                        string devName;
                                        HardwareMonitor.ClassifyDevice(sensor, label, out devCat, out devId, out devName);

                                        readings.Add(new TemperatureReading(
                                            devName,
                                            label,
                                            tempVal,
                                            devCat == DeviceCategory.Cpu ? "Temperature" : "GPU",
                                            devCat,
                                            devId,
                                            devName
                                        ));
                                    }
                                }
                            }
                        }
                        index++;
                    }

                    if (readings.Count > 0) return readings;
                }
            }
            catch
            {
            }

            return null;
        }

        private static bool IsTemperatureSensor(string category, string label, string unit, out bool isGpu)
        {
            isGpu = false;
            if (string.IsNullOrEmpty(label)) return false;

            // 1. 単位や名称から使用率（%）やクロック、ディスク、電圧、ファン等を除外
            if (!string.IsNullOrEmpty(unit) && (unit.IndexOf("%") >= 0 || unit.IndexOf("RPM", StringComparison.OrdinalIgnoreCase) >= 0 || unit.IndexOf("V", StringComparison.OrdinalIgnoreCase) >= 0 || unit.IndexOf("W", StringComparison.OrdinalIgnoreCase) >= 0)) return false;
            if (label.IndexOf("使用率", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (label.IndexOf("在留率", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (label.IndexOf("Residency", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (label.IndexOf("Usage", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (label.IndexOf("Clock", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (label.IndexOf("Power", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (label.IndexOf("Voltage", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (label.IndexOf("制限", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (label.IndexOf("Limit", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (label.IndexOf("Target", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (label.IndexOf("Distance", StringComparison.OrdinalIgnoreCase) >= 0) return false;

            if (!string.IsNullOrEmpty(category))
            {
                if (category.IndexOf("Drive", StringComparison.OrdinalIgnoreCase) >= 0) return false;
                if (category.IndexOf("ディスク", StringComparison.OrdinalIgnoreCase) >= 0) return false;
                if (category.IndexOf("Network", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            }

            // 2. GPU 判定 (GeForce, Radeon, GPU)
            bool catIsGpu = !string.IsNullOrEmpty(category) && (
                category.IndexOf("GPU", StringComparison.OrdinalIgnoreCase) >= 0 ||
                category.IndexOf("GeForce", StringComparison.OrdinalIgnoreCase) >= 0 ||
                category.IndexOf("Radeon", StringComparison.OrdinalIgnoreCase) >= 0
            );
            bool labelIsGpu = label.IndexOf("GPU", StringComparison.OrdinalIgnoreCase) >= 0 ||
                              label.IndexOf("Hot Spot", StringComparison.OrdinalIgnoreCase) >= 0;

            if (catIsGpu || labelIsGpu)
            {
                if (label.IndexOf("Temp", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    label.IndexOf("温度", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    label.IndexOf("Hot Spot", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    label.IndexOf("Memory Junction", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    label.IndexOf("Core", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (!string.IsNullOrEmpty(unit) && (unit.IndexOf("C", StringComparison.OrdinalIgnoreCase) >= 0 || unit.IndexOf("℃") >= 0)))
                {
                    isGpu = true;
                    return true;
                }
                return false;
            }

            // 3. CPU 判定
            if (!string.IsNullOrEmpty(category) && category.IndexOf("CPU", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (label.IndexOf("Core", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (label.IndexOf("CCD", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (label.IndexOf("Tdie", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (label.IndexOf("Tctl", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (label.IndexOf("L3", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (label.IndexOf("IOD", StringComparison.OrdinalIgnoreCase) >= 0) return true;

            return false;
        }

        private static T ReadStruct<T>(MemoryMappedViewAccessor accessor, long position) where T : struct
        {
            int size = Marshal.SizeOf(typeof(T));
            byte[] buffer = new byte[size];
            accessor.ReadArray(position, buffer, 0, size);

            GCHandle handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try
            {
                return (T)Marshal.PtrToStructure(handle.AddrOfPinnedObject(), typeof(T));
            }
            finally
            {
                handle.Free();
            }
        }
    }
}
