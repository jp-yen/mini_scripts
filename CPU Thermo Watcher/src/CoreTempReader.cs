// --------------------------------------------------------------------------------------------------------------------
// <copyright file="CoreTempReader.cs" company="CPU Thermo Watcher Contributors">
//   Copyright (c) 2026, CPU Thermo Watcher Contributors. All rights reserved.
//   Licensed under the 3-Clause BSD License.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Text;

namespace CpuThermoWatcher
{
    public static class CoreTempReader
    {
        private const string MappingNameEx = "CoreTempMappingObjectEx";
        private const string MappingNameOld = "CoreTempMappingObject";

        [StructLayout(LayoutKind.Sequential, Pack = 4, CharSet = CharSet.Ansi)]
        private struct CoreTempSharedDataEx
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
            public uint[] uiLoad;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 128)]
            public uint[] uiTjMax;

            public uint uiCoreCnt;
            public uint uiCPUCnt;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
            public float[] fTemp;

            public float fVID;
            public float fCPUSpeed;
            public float fFSBSpeed;
            public float fMultiplier;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 100)]
            public byte[] sCPUNameBytes;

            public byte ucFahrenheit;
            public byte ucDeltaToTjMax;
            public byte ucTdpSupported;
            public byte ucPowerSupported;

            public uint uiStructVersion;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 128)]
            public uint[] uiTdp;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 128)]
            public float[] fPower;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
            public float[] fMultipliers;
        }

        public static List<TemperatureReading> TryReadCoreTemp()
        {
            string[] names = new string[] { MappingNameEx, MappingNameOld };

            foreach (string name in names)
            {
                try
                {
                    using (MemoryMappedFile mmf = MemoryMappedFile.OpenExisting(name, MemoryMappedFileRights.Read))
                    {
                        using (MemoryMappedViewAccessor accessor = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read))
                        {
                            int baseSize = Marshal.SizeOf(typeof(CoreTempSharedDataEx));
                            if (accessor.Capacity < baseSize)
                            {
                                continue;
                            }

                            byte[] rawBytes = new byte[baseSize];
                            accessor.ReadArray(0, rawBytes, 0, baseSize);

                            GCHandle handle = GCHandle.Alloc(rawBytes, GCHandleType.Pinned);
                            try
                            {
                                CoreTempSharedDataEx data = (CoreTempSharedDataEx)Marshal.PtrToStructure(
                                    handle.AddrOfPinnedObject(), typeof(CoreTempSharedDataEx));

                                if (data.uiCoreCnt > 0 && data.uiCoreCnt <= 256)
                                {
                                    string cpuName = GetCpuName(data.sCPUNameBytes);
                                    if (string.IsNullOrEmpty(cpuName)) cpuName = "CPU";

                                    List<TemperatureReading> list = new List<TemperatureReading>();

                                    // 1. 各コア温度 (Core #0 .. Core #N)
                                    for (int i = 0; i < data.uiCoreCnt; i++)
                                    {
                                        float t = data.fTemp[i];
                                        if (t > 0f && t < 150f)
                                        {
                                            list.Add(new TemperatureReading(
                                                cpuName,
                                                string.Format("コア #{0}", i),
                                                t,
                                                "Core"
                                            ));
                                        }
                                    }

                                    // 2. 拡張フィールド (Tdie / Tccd / Tctl) の探索
                                    // baseSize 以降に float 配列が存在するか確認
                                    long extraOffset = baseSize;
                                    if (accessor.Capacity >= extraOffset + 72)
                                    {
                                        try
                                        {
                                            // Tccd[16] (16 * 4 = 64 bytes)
                                            for (int c = 0; c < 8; c++)
                                            {
                                                float tccd = accessor.ReadSingle(extraOffset + c * 4);
                                                if (tccd > 10f && tccd < 130f)
                                                {
                                                    list.Insert(0, new TemperatureReading(cpuName, string.Format("Tccd #{0}", c), tccd, "Die"));
                                                }
                                            }

                                            // Tdie
                                            float tdie = accessor.ReadSingle(extraOffset + 64);
                                            if (tdie > 10f && tdie < 130f)
                                            {
                                                list.Insert(0, new TemperatureReading(cpuName, "Tdie", tdie, "Package"));
                                            }
                                        }
                                        catch
                                        {
                                        }
                                    }

                                    if (list.Count > 0)
                                    {
                                        return list;
                                    }
                                }
                            }
                            finally
                            {
                                handle.Free();
                            }
                        }
                    }
                }
                catch
                {
                    // 未起動または共有メモリ未作成時はスキップ
                }
            }

            return null;
        }

        private static string GetCpuName(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return string.Empty;
            int len = Array.IndexOf(bytes, (byte)0);
            if (len < 0) len = bytes.Length;
            return Encoding.ASCII.GetString(bytes, 0, len).Trim();
        }
    }
}
