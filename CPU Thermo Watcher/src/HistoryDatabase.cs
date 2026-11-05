// --------------------------------------------------------------------------------------------------------------------
// <copyright file="HistoryDatabase.cs" company="CPU Thermo Watcher Contributors">
//   Copyright (c) 2026, CPU Thermo Watcher Contributors. All rights reserved.
//   Licensed under the 3-Clause BSD License.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace CpuThermoWatcher
{
    public class HistoryRecord
    {
        public long Id { get; set; }
        public DateTime Timestamp { get; set; }
        public string DeviceId { get; set; }
        public string DeviceName { get; set; }
        public string SensorName { get; set; }
        public float Temperature { get; set; }
        public bool IsDeviceMax { get; set; }
        public bool IsWarning { get; set; }
    }

    public static class HistoryDatabase
    {
        private const string DbFileName = "history.db";
        private static readonly object _dbLock = new object();
        private static DateTime _lastRegularLogTime = DateTime.MinValue;
        private static DateTime _lastWarningLogTime = DateTime.MinValue;

        #region SQLite Native P/Invoke (winsqlite3.dll - Windows 10/11 Built-in)

        private const int SQLITE_OK = 0;
        private const int SQLITE_ROW = 100;
        private const int SQLITE_DONE = 101;

        [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_open16", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_open16(string filename, out IntPtr db);

        [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_close", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_close(IntPtr db);

        [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_prepare16_v2", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_prepare16_v2(IntPtr db, string sql, int numBytes, out IntPtr stmt, IntPtr pzTail);

        [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_step", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_step(IntPtr stmt);

        [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_finalize", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_finalize(IntPtr stmt);

        [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_reset", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_reset(IntPtr stmt);

        [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_bind_text16", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_bind_text16(IntPtr stmt, int index, string text, int len, IntPtr destructor);

        [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_bind_double", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_bind_double(IntPtr stmt, int index, double val);

        [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_bind_int", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_bind_int(IntPtr stmt, int index, int val);

        [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_column_int64", CallingConvention = CallingConvention.Cdecl)]
        private static extern long sqlite3_column_int64(IntPtr stmt, int col);

        [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_column_int", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_column_int(IntPtr stmt, int col);

        [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_column_double", CallingConvention = CallingConvention.Cdecl)]
        private static extern double sqlite3_column_double(IntPtr stmt, int col);

        [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_column_text16", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr sqlite3_column_text16(IntPtr stmt, int col);

        [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_exec", CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_exec(IntPtr db, string sql, IntPtr callback, IntPtr arg, out IntPtr errmsg);

        #endregion

        public static string GetDatabasePath()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            return Path.Combine(baseDir, DbFileName);
        }

        public static void Initialize()
        {
            lock (_dbLock)
            {
                IntPtr db;
                string dbPath = GetDatabasePath();
                if (sqlite3_open16(dbPath, out db) != SQLITE_OK) return;

                try
                {
                    IntPtr err;
                    sqlite3_exec(db, "PRAGMA journal_mode = WAL;", IntPtr.Zero, IntPtr.Zero, out err);
                    sqlite3_exec(db, "PRAGMA synchronous = NORMAL;", IntPtr.Zero, IntPtr.Zero, out err);
                    sqlite3_exec(db, "PRAGMA temp_store = MEMORY;", IntPtr.Zero, IntPtr.Zero, out err);

                    string schemaSql =
                        "CREATE TABLE IF NOT EXISTS sensor_logs (" +
                        "  id INTEGER PRIMARY KEY AUTOINCREMENT," +
                        "  timestamp TEXT NOT NULL," +
                        "  device_id TEXT NOT NULL," +
                        "  device_name TEXT NOT NULL," +
                        "  sensor_name TEXT NOT NULL," +
                        "  temperature REAL NOT NULL," +
                        "  is_device_max INTEGER NOT NULL DEFAULT 0," +
                        "  is_warning INTEGER NOT NULL DEFAULT 0," +
                        "  UNIQUE(timestamp, device_id, sensor_name)" +
                        ");" +
                        "CREATE INDEX IF NOT EXISTS idx_sensor_logs_time ON sensor_logs(timestamp);" +
                        "CREATE INDEX IF NOT EXISTS idx_sensor_logs_dev_max ON sensor_logs(is_device_max, timestamp);" +
                        "CREATE INDEX IF NOT EXISTS idx_sensor_logs_device ON sensor_logs(device_id);" +
                        "CREATE INDEX IF NOT EXISTS idx_sensor_logs_sensor ON sensor_logs(sensor_name);";

                    sqlite3_exec(db, schemaSql, IntPtr.Zero, IntPtr.Zero, out err);
                }
                finally
                {
                    sqlite3_close(db);
                }
            }
        }

        public static bool LogSnapshot(CpuTemperatureSnapshot snapshot, Settings settings)
        {
            if (snapshot == null || !snapshot.Success || snapshot.Sensors.Count == 0)
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
            bool shouldWrite = false;
            bool isWarningWrite = false;

            // 1. 高温警告（最低30秒間隔）
            if (isOverThreshold && (now - _lastWarningLogTime).TotalSeconds >= 30)
            {
                shouldWrite = true;
                isWarningWrite = true;
                _lastWarningLogTime = now;
                _lastRegularLogTime = now;
            }
            // 2. 定期記録
            else if ((now - _lastRegularLogTime).TotalSeconds >= settings.EventLogIntervalSeconds)
            {
                shouldWrite = true;
                _lastRegularLogTime = now;
            }

            if (!shouldWrite) return false;

            return WriteSnapshotToDb(snapshot, isWarningWrite);
        }

        private static bool WriteSnapshotToDb(CpuTemperatureSnapshot snapshot, bool isWarning)
        {
            lock (_dbLock)
            {
                IntPtr db;
                string dbPath = GetDatabasePath();
                if (sqlite3_open16(dbPath, out db) != SQLITE_OK) return false;

                IntPtr stmt = IntPtr.Zero;
                try
                {
                    IntPtr err;
                    sqlite3_exec(db, "BEGIN TRANSACTION;", IntPtr.Zero, IntPtr.Zero, out err);

                    string insertSql = "INSERT OR IGNORE INTO sensor_logs " +
                        "(timestamp, device_id, device_name, sensor_name, temperature, is_device_max, is_warning) " +
                        "VALUES (?, ?, ?, ?, ?, ?, ?);";

                    if (sqlite3_prepare16_v2(db, insertSql, -1, out stmt, IntPtr.Zero) != SQLITE_OK)
                    {
                        sqlite3_exec(db, "ROLLBACK;", IntPtr.Zero, IntPtr.Zero, out err);
                        return false;
                    }

                    string timeStr = snapshot.Timestamp.ToString("yyyy-MM-dd HH:mm:ss");
                    int warnInt = isWarning ? 1 : 0;

                    // 各デバイスの最高温度センサー名を取得
                    Dictionary<string, string> maxSensorNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    if (snapshot.Devices != null)
                    {
                        for (int i = 0; i < snapshot.Devices.Count; i++)
                        {
                            HardwareDeviceSummary dev = snapshot.Devices[i];
                            if (dev.MaxSensor != null && !string.IsNullOrEmpty(dev.DeviceId))
                            {
                                maxSensorNames[dev.DeviceId] = dev.MaxSensor.SensorName;
                            }
                        }
                    }

                    HashSet<string> markedMaxDevices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    // 全個別センサーを 1 回ずつインサート (最高温度センサーの行に is_device_max = 1 を付与、二重レコードなし)
                    if (snapshot.Sensors != null)
                    {
                        for (int i = 0; i < snapshot.Sensors.Count; i++)
                        {
                            TemperatureReading r = snapshot.Sensors[i];
                            string devId = r.DeviceId ?? "CPU";

                            int isMax = 0;
                            string targetMaxSensor;
                            if (maxSensorNames.TryGetValue(devId, out targetMaxSensor))
                            {
                                if (!markedMaxDevices.Contains(devId) && string.Equals(r.SensorName, targetMaxSensor, StringComparison.OrdinalIgnoreCase))
                                {
                                    isMax = 1;
                                    markedMaxDevices.Add(devId);
                                }
                            }

                            sqlite3_reset(stmt);
                            sqlite3_bind_text16(stmt, 1, timeStr, -1, IntPtr.Zero);
                            sqlite3_bind_text16(stmt, 2, devId, -1, IntPtr.Zero);
                            sqlite3_bind_text16(stmt, 3, r.DeviceName ?? r.HardwareName ?? devId, -1, IntPtr.Zero);
                            sqlite3_bind_text16(stmt, 4, r.SensorName, -1, IntPtr.Zero);
                            sqlite3_bind_double(stmt, 5, r.Value);
                            sqlite3_bind_int(stmt, 6, isMax);
                            sqlite3_bind_int(stmt, 7, warnInt);
                            sqlite3_step(stmt);
                        }
                    }

                    sqlite3_finalize(stmt);
                    stmt = IntPtr.Zero;

                    sqlite3_exec(db, "COMMIT;", IntPtr.Zero, IntPtr.Zero, out err);
                    return true;
                }
                catch
                {
                    if (stmt != IntPtr.Zero)
                    {
                        sqlite3_finalize(stmt);
                    }
                    IntPtr err;
                    sqlite3_exec(db, "ROLLBACK;", IntPtr.Zero, IntPtr.Zero, out err);
                    return false;
                }
                finally
                {
                    sqlite3_close(db);
                }
            }
        }

        public static List<TemperatureLogEntry> ReadLogs(DateTime? startTime, DateTime? endTime)
        {
            List<TemperatureLogEntry> list = new List<TemperatureLogEntry>();
            lock (_dbLock)
            {
                IntPtr db;
                string dbPath = GetDatabasePath();
                if (!File.Exists(dbPath)) return list;
                if (sqlite3_open16(dbPath, out db) != SQLITE_OK) return list;

                IntPtr stmt = IntPtr.Zero;
                try
                {
                    StringBuilder sql = new StringBuilder();
                    sql.Append("SELECT timestamp, device_id, device_name, sensor_name, temperature, is_warning ");
                    sql.Append("FROM sensor_logs WHERE is_device_max = 1 ");

                    if (startTime.HasValue)
                    {
                        sql.Append("AND timestamp >= ? ");
                    }
                    if (endTime.HasValue)
                    {
                        sql.Append("AND timestamp <= ? ");
                    }
                    sql.Append("ORDER BY timestamp ASC;");

                    if (sqlite3_prepare16_v2(db, sql.ToString(), -1, out stmt, IntPtr.Zero) != SQLITE_OK)
                    {
                        return list;
                    }

                    int bindIdx = 1;
                    if (startTime.HasValue)
                    {
                        sqlite3_bind_text16(stmt, bindIdx++, startTime.Value.ToString("yyyy-MM-dd HH:mm:ss"), -1, IntPtr.Zero);
                    }
                    if (endTime.HasValue)
                    {
                        sqlite3_bind_text16(stmt, bindIdx++, endTime.Value.ToString("yyyy-MM-dd HH:mm:ss"), -1, IntPtr.Zero);
                    }

                    Dictionary<string, TemperatureLogEntry> map = new Dictionary<string, TemperatureLogEntry>();

                    while (sqlite3_step(stmt) == SQLITE_ROW)
                    {
                        string timeStr = Marshal.PtrToStringUni(sqlite3_column_text16(stmt, 0));
                        string devId = Marshal.PtrToStringUni(sqlite3_column_text16(stmt, 1));
                        string devName = Marshal.PtrToStringUni(sqlite3_column_text16(stmt, 2));
                        string sensorName = Marshal.PtrToStringUni(sqlite3_column_text16(stmt, 3));
                        float temp = (float)sqlite3_column_double(stmt, 4);
                        bool isWarning = sqlite3_column_int(stmt, 5) != 0;

                        TemperatureLogEntry entry;
                        if (!map.TryGetValue(timeStr, out entry))
                        {
                            DateTime dt;
                            if (!DateTime.TryParseExact(timeStr, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
                            {
                                dt = DateTime.Now;
                            }
                            entry = new TemperatureLogEntry();
                            entry.Timestamp = dt;
                            map[timeStr] = entry;
                        }

                        if (isWarning) entry.IsWarning = true;

                        if (string.Equals(devId, "CPU", StringComparison.OrdinalIgnoreCase))
                        {
                            entry.MaxTemperature = temp;
                            entry.MaxSensorName = sensorName;
                        }
                        else if (string.Equals(devId, "iGPU", StringComparison.OrdinalIgnoreCase))
                        {
                            entry.IGpuTemperature = temp;
                            entry.IGpuSensorName = sensorName;
                        }
                        else if (devId != null && devId.StartsWith("dGPU", StringComparison.OrdinalIgnoreCase))
                        {
                            entry.DGpuTemperatures[devId] = temp;
                            entry.DGpuSensorNames[devId] = sensorName;
                        }
                    }

                    list.AddRange(map.Values);
                }
                finally
                {
                    if (stmt != IntPtr.Zero)
                    {
                        sqlite3_finalize(stmt);
                    }
                    sqlite3_close(db);
                }
            }

            return list;
        }

        public static List<HistoryRecord> QueryRecords(
            DateTime? startTime,
            DateTime? endTime,
            string deviceId,
            string sensorFilter,
            bool onlyDeviceMax,
            int limit)
        {
            List<HistoryRecord> list = new List<HistoryRecord>();
            lock (_dbLock)
            {
                IntPtr db;
                string dbPath = GetDatabasePath();
                if (!File.Exists(dbPath)) return list;
                if (sqlite3_open16(dbPath, out db) != SQLITE_OK) return list;

                IntPtr stmt = IntPtr.Zero;
                try
                {
                    StringBuilder sql = new StringBuilder();
                    sql.Append("SELECT id, timestamp, device_id, device_name, sensor_name, temperature, is_device_max, is_warning ");
                    sql.Append("FROM sensor_logs WHERE 1=1 ");

                    if (startTime.HasValue) sql.Append("AND timestamp >= ? ");
                    if (endTime.HasValue) sql.Append("AND timestamp <= ? ");

                    if (!string.IsNullOrEmpty(deviceId) && !deviceId.Equals("すべて", StringComparison.OrdinalIgnoreCase))
                    {
                        sql.Append("AND device_id = ? ");
                    }

                    if (onlyDeviceMax)
                    {
                        sql.Append("AND is_device_max = 1 ");
                    }

                    if (!string.IsNullOrEmpty(sensorFilter) && !sensorFilter.Equals("すべて", StringComparison.OrdinalIgnoreCase))
                    {
                        sql.Append("AND sensor_name LIKE ? ");
                    }

                    sql.Append("ORDER BY timestamp DESC, device_id ASC, temperature DESC ");
                    if (limit > 0)
                    {
                        sql.Append("LIMIT ? ");
                    }
                    sql.Append(";");

                    if (sqlite3_prepare16_v2(db, sql.ToString(), -1, out stmt, IntPtr.Zero) != SQLITE_OK)
                    {
                        return list;
                    }

                    int bindIdx = 1;
                    if (startTime.HasValue)
                    {
                        sqlite3_bind_text16(stmt, bindIdx++, startTime.Value.ToString("yyyy-MM-dd HH:mm:ss"), -1, IntPtr.Zero);
                    }
                    if (endTime.HasValue)
                    {
                        sqlite3_bind_text16(stmt, bindIdx++, endTime.Value.ToString("yyyy-MM-dd HH:mm:ss"), -1, IntPtr.Zero);
                    }
                    if (!string.IsNullOrEmpty(deviceId) && !deviceId.Equals("すべて", StringComparison.OrdinalIgnoreCase))
                    {
                        sqlite3_bind_text16(stmt, bindIdx++, deviceId, -1, IntPtr.Zero);
                    }
                    if (!string.IsNullOrEmpty(sensorFilter) && !sensorFilter.Equals("すべて", StringComparison.OrdinalIgnoreCase))
                    {
                        sqlite3_bind_text16(stmt, bindIdx++, "%" + sensorFilter + "%", -1, IntPtr.Zero);
                    }
                    if (limit > 0)
                    {
                        sqlite3_bind_int(stmt, bindIdx++, limit);
                    }

                    while (sqlite3_step(stmt) == SQLITE_ROW)
                    {
                        long id = sqlite3_column_int64(stmt, 0);
                        string timeStr = Marshal.PtrToStringUni(sqlite3_column_text16(stmt, 1));
                        string devId = Marshal.PtrToStringUni(sqlite3_column_text16(stmt, 2));
                        string devName = Marshal.PtrToStringUni(sqlite3_column_text16(stmt, 3));
                        string sensorName = Marshal.PtrToStringUni(sqlite3_column_text16(stmt, 4));
                        float temp = (float)sqlite3_column_double(stmt, 5);
                        bool isMax = sqlite3_column_int(stmt, 6) != 0;
                        bool isWarn = sqlite3_column_int(stmt, 7) != 0;

                        DateTime dt;
                        DateTime.TryParseExact(timeStr, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out dt);

                        HistoryRecord rec = new HistoryRecord
                        {
                            Id = id,
                            Timestamp = dt,
                            DeviceId = devId,
                            DeviceName = devName,
                            SensorName = sensorName,
                            Temperature = temp,
                            IsDeviceMax = isMax,
                            IsWarning = isWarn
                        };
                        list.Add(rec);
                    }
                }
                finally
                {
                    if (stmt != IntPtr.Zero)
                    {
                        sqlite3_finalize(stmt);
                    }
                    sqlite3_close(db);
                }
            }

            return list;
        }

        public static List<string> GetDistinctDevices()
        {
            List<string> list = new List<string>();
            list.Add("すべて");

            lock (_dbLock)
            {
                IntPtr db;
                string dbPath = GetDatabasePath();
                if (!File.Exists(dbPath)) return list;
                if (sqlite3_open16(dbPath, out db) != SQLITE_OK) return list;

                IntPtr stmt = IntPtr.Zero;
                try
                {
                    string sql = "SELECT DISTINCT device_id FROM sensor_logs ORDER BY device_id ASC;";
                    if (sqlite3_prepare16_v2(db, sql, -1, out stmt, IntPtr.Zero) == SQLITE_OK)
                    {
                        while (sqlite3_step(stmt) == SQLITE_ROW)
                        {
                            string d = Marshal.PtrToStringUni(sqlite3_column_text16(stmt, 0));
                            if (!string.IsNullOrEmpty(d) && !list.Contains(d))
                            {
                                list.Add(d);
                            }
                        }
                    }
                }
                finally
                {
                    if (stmt != IntPtr.Zero)
                    {
                        sqlite3_finalize(stmt);
                    }
                    sqlite3_close(db);
                }
            }

            return list;
        }

        public static List<string> GetDistinctSensors(string deviceId)
        {
            List<string> list = new List<string>();
            list.Add("すべて");

            lock (_dbLock)
            {
                IntPtr db;
                string dbPath = GetDatabasePath();
                if (!File.Exists(dbPath)) return list;
                if (sqlite3_open16(dbPath, out db) != SQLITE_OK) return list;

                IntPtr stmt = IntPtr.Zero;
                try
                {
                    StringBuilder sql = new StringBuilder("SELECT DISTINCT sensor_name FROM sensor_logs ");
                    if (!string.IsNullOrEmpty(deviceId) && !deviceId.Equals("すべて", StringComparison.OrdinalIgnoreCase))
                    {
                        sql.Append("WHERE device_id = ? ");
                    }
                    sql.Append("ORDER BY sensor_name ASC;");

                    if (sqlite3_prepare16_v2(db, sql.ToString(), -1, out stmt, IntPtr.Zero) == SQLITE_OK)
                    {
                        if (!string.IsNullOrEmpty(deviceId) && !deviceId.Equals("すべて", StringComparison.OrdinalIgnoreCase))
                        {
                            sqlite3_bind_text16(stmt, 1, deviceId, -1, IntPtr.Zero);
                        }

                        while (sqlite3_step(stmt) == SQLITE_ROW)
                        {
                            string s = Marshal.PtrToStringUni(sqlite3_column_text16(stmt, 0));
                            if (!string.IsNullOrEmpty(s) && !list.Contains(s))
                            {
                                list.Add(s);
                            }
                        }
                    }
                }
                finally
                {
                    if (stmt != IntPtr.Zero)
                    {
                        sqlite3_finalize(stmt);
                    }
                    sqlite3_close(db);
                }
            }

            return list;
        }

        public static int ExportCsv(
            string filePath,
            DateTime? startTime,
            DateTime? endTime,
            string deviceId,
            string sensorFilter,
            bool onlyDeviceMax)
        {
            List<HistoryRecord> records = QueryRecords(startTime, endTime, deviceId, sensorFilter, onlyDeviceMax, limit: 0);
            using (StreamWriter sw = new StreamWriter(filePath, false, new UTF8Encoding(true)))
            {
                sw.WriteLine("ID,日時,デバイスID,デバイス名,センサー名,温度(℃),代表最高温度,警告状態");
                for (int i = 0; i < records.Count; i++)
                {
                    HistoryRecord r = records[i];
                    string line = string.Format(CultureInfo.InvariantCulture,
                        "{0},\"{1:yyyy-MM-dd HH:mm:ss}\",\"{2}\",\"{3}\",\"{4}\",{5:F1},{6},{7}",
                        r.Id,
                        r.Timestamp,
                        EscapeCsv(r.DeviceId),
                        EscapeCsv(r.DeviceName),
                        EscapeCsv(r.SensorName),
                        r.Temperature,
                        r.IsDeviceMax ? 1 : 0,
                        r.IsWarning ? 1 : 0);
                    sw.WriteLine(line);
                }
            }
            return records.Count;
        }

        private static string EscapeCsv(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Replace("\"", "\"\"");
        }
    }
}
