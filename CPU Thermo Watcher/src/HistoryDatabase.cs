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

    public class CpuCompositeLogEntry
    {
        public DateTime Timestamp { get; set; }
        public float? CpuTemperature { get; set; }
        public float? MaxClock { get; set; }
        public float? AvgClock { get; set; }
        public Dictionary<string, float> CoreClocks { get; set; }

        public CpuCompositeLogEntry()
        {
            CoreClocks = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public static class HistoryDatabase
    {
        private const string DbFileName = "history.db";
        private const string TimestampFormat = "yyyy-MM-dd HH:mm:ss";
        private const string FilterAllLabel = "すべて";
        private static readonly object _dbLock = new object();
        private static DateTime _lastRegularLogTime = DateTime.MinValue;
        private static DateTime _lastWarningLogTime = DateTime.MinValue;

        #region SQLite Native P/Invoke (winsqlite3.dll - Windows 10/11 Built-in)

        private const int SQLITE_OK = 0;
        private const int SQLITE_ROW = 100;

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
                        "CREATE INDEX IF NOT EXISTS idx_sensor_logs_sensor ON sensor_logs(sensor_name);" +
                        "CREATE TABLE IF NOT EXISTS cpu_clock_logs (" +
                        "  id INTEGER PRIMARY KEY AUTOINCREMENT," +
                        "  timestamp TEXT NOT NULL," +
                        "  core_name TEXT NOT NULL," +
                        "  clock_mhz REAL NOT NULL," +
                        "  is_max INTEGER NOT NULL DEFAULT 0," +
                        "  UNIQUE(timestamp, core_name)" +
                        ");" +
                        "CREATE INDEX IF NOT EXISTS idx_clock_logs_time ON cpu_clock_logs(timestamp);" +
                        "CREATE INDEX IF NOT EXISTS idx_clock_logs_core ON cpu_clock_logs(core_name);";

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
            bool isOverThreshold = snapshot.IsAnyDeviceOverThreshold(settings);
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

                try
                {
                    IntPtr err;
                    sqlite3_exec(db, "BEGIN TRANSACTION;", IntPtr.Zero, IntPtr.Zero, out err);

                    string timeStr = snapshot.Timestamp.ToString(TimestampFormat);
                    if (!InsertTemperatureSensors(db, snapshot, timeStr, isWarning))
                    {
                        sqlite3_exec(db, "ROLLBACK;", IntPtr.Zero, IntPtr.Zero, out err);
                        return false;
                    }

                    InsertCpuClocks(db, snapshot.CpuClocks, timeStr);

                    sqlite3_exec(db, "COMMIT;", IntPtr.Zero, IntPtr.Zero, out err);
                    return true;
                }
                catch
                {
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

        private static bool InsertTemperatureSensors(IntPtr db, CpuTemperatureSnapshot snapshot, string timeStr, bool isWarning)
        {
            string insertSql = "INSERT OR IGNORE INTO sensor_logs " +
                "(timestamp, device_id, device_name, sensor_name, temperature, is_device_max, is_warning) " +
                "VALUES (?, ?, ?, ?, ?, ?, ?);";

            IntPtr stmt = IntPtr.Zero;
            if (sqlite3_prepare16_v2(db, insertSql, -1, out stmt, IntPtr.Zero) != SQLITE_OK)
            {
                return false;
            }

            try
            {
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

                return true;
            }
            finally
            {
                sqlite3_finalize(stmt);
            }
        }

        private static void InsertCpuClocks(IntPtr db, List<ClockReading> clocks, string timeStr)
        {
            if (clocks == null || clocks.Count == 0) return;

            string insertClockSql = "INSERT OR IGNORE INTO cpu_clock_logs " +
                "(timestamp, core_name, clock_mhz, is_max) " +
                "VALUES (?, ?, ?, ?);";

            IntPtr stmt = IntPtr.Zero;
            if (sqlite3_prepare16_v2(db, insertClockSql, -1, out stmt, IntPtr.Zero) != SQLITE_OK)
            {
                return;
            }

            try
            {
                // 最高クロックのコアを特定
                float maxClockVal = -1f;
                string maxCoreName = null;
                for (int i = 0; i < clocks.Count; i++)
                {
                    if (clocks[i].Value > maxClockVal)
                    {
                        maxClockVal = clocks[i].Value;
                        maxCoreName = clocks[i].CoreName;
                    }
                }

                bool markedMaxClock = false;
                for (int i = 0; i < clocks.Count; i++)
                {
                    ClockReading c = clocks[i];
                    int isClockMax = 0;
                    if (!markedMaxClock && string.Equals(c.CoreName, maxCoreName, StringComparison.OrdinalIgnoreCase))
                    {
                        isClockMax = 1;
                        markedMaxClock = true;
                    }

                    sqlite3_reset(stmt);
                    sqlite3_bind_text16(stmt, 1, timeStr, -1, IntPtr.Zero);
                    sqlite3_bind_text16(stmt, 2, c.CoreName, -1, IntPtr.Zero);
                    sqlite3_bind_double(stmt, 3, c.Value);
                    sqlite3_bind_int(stmt, 4, isClockMax);
                    sqlite3_step(stmt);
                }
            }
            finally
            {
                sqlite3_finalize(stmt);
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
                    sql.Append("SELECT timestamp, device_id, sensor_name, temperature, is_warning ");
                    sql.Append("FROM sensor_logs WHERE is_device_max = 1 ");
                    AppendTimeRangeClause(sql, startTime, endTime);
                    sql.Append("ORDER BY timestamp ASC;");

                    if (sqlite3_prepare16_v2(db, sql.ToString(), -1, out stmt, IntPtr.Zero) != SQLITE_OK)
                    {
                        return list;
                    }

                    int bindIdx = 1;
                    BindTimeRange(stmt, ref bindIdx, startTime, endTime);

                    Dictionary<string, TemperatureLogEntry> map = new Dictionary<string, TemperatureLogEntry>();

                    while (sqlite3_step(stmt) == SQLITE_ROW)
                    {
                        string timeStr = GetColumnText(stmt, 0);
                        string devId = GetColumnText(stmt, 1);
                        string sensorName = GetColumnText(stmt, 2);
                        float temp = (float)sqlite3_column_double(stmt, 3);
                        bool isWarning = sqlite3_column_int(stmt, 4) != 0;

                        TemperatureLogEntry entry;
                        if (!map.TryGetValue(timeStr, out entry))
                        {
                            entry = new TemperatureLogEntry { Timestamp = ParseTimestamp(timeStr) };
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
                    if (stmt != IntPtr.Zero) sqlite3_finalize(stmt);
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
                    bool filterDevice = !string.IsNullOrEmpty(deviceId) && !deviceId.Equals(FilterAllLabel, StringComparison.OrdinalIgnoreCase);
                    bool filterSensor = !string.IsNullOrEmpty(sensorFilter) && !sensorFilter.Equals(FilterAllLabel, StringComparison.OrdinalIgnoreCase);

                    StringBuilder sql = new StringBuilder();
                    sql.Append("SELECT id, timestamp, device_id, device_name, sensor_name, temperature, is_device_max, is_warning ");
                    sql.Append("FROM sensor_logs WHERE 1=1 ");
                    AppendTimeRangeClause(sql, startTime, endTime);

                    if (filterDevice) sql.Append("AND device_id = ? ");
                    if (onlyDeviceMax) sql.Append("AND is_device_max = 1 ");
                    if (filterSensor) sql.Append("AND sensor_name LIKE ? ");

                    sql.Append("ORDER BY timestamp DESC, device_id ASC, temperature DESC ");
                    if (limit > 0) sql.Append("LIMIT ? ");
                    sql.Append(";");

                    if (sqlite3_prepare16_v2(db, sql.ToString(), -1, out stmt, IntPtr.Zero) != SQLITE_OK)
                    {
                        return list;
                    }

                    int bindIdx = 1;
                    BindTimeRange(stmt, ref bindIdx, startTime, endTime);

                    if (filterDevice)
                    {
                        sqlite3_bind_text16(stmt, bindIdx++, deviceId, -1, IntPtr.Zero);
                    }
                    if (filterSensor)
                    {
                        sqlite3_bind_text16(stmt, bindIdx++, "%" + sensorFilter + "%", -1, IntPtr.Zero);
                    }
                    if (limit > 0)
                    {
                        sqlite3_bind_int(stmt, bindIdx++, limit);
                    }

                    while (sqlite3_step(stmt) == SQLITE_ROW)
                    {
                        HistoryRecord rec = new HistoryRecord
                        {
                            Id = sqlite3_column_int64(stmt, 0),
                            Timestamp = ParseTimestamp(GetColumnText(stmt, 1)),
                            DeviceId = GetColumnText(stmt, 2),
                            DeviceName = GetColumnText(stmt, 3),
                            SensorName = GetColumnText(stmt, 4),
                            Temperature = (float)sqlite3_column_double(stmt, 5),
                            IsDeviceMax = sqlite3_column_int(stmt, 6) != 0,
                            IsWarning = sqlite3_column_int(stmt, 7) != 0
                        };
                        list.Add(rec);
                    }
                }
                finally
                {
                    if (stmt != IntPtr.Zero) sqlite3_finalize(stmt);
                    sqlite3_close(db);
                }
            }

            return list;
        }

        public static List<string> GetDistinctDevices()
        {
            List<string> list = new List<string> { FilterAllLabel };
            ReadDistinctColumn("SELECT DISTINCT device_id FROM sensor_logs ORDER BY device_id ASC;", null, list);
            return list;
        }

        public static List<string> GetDistinctSensors(string deviceId)
        {
            List<string> list = new List<string> { FilterAllLabel };
            bool filterDevice = !string.IsNullOrEmpty(deviceId) && !deviceId.Equals(FilterAllLabel, StringComparison.OrdinalIgnoreCase);

            string sql = filterDevice
                ? "SELECT DISTINCT sensor_name FROM sensor_logs WHERE device_id = ? ORDER BY sensor_name ASC;"
                : "SELECT DISTINCT sensor_name FROM sensor_logs ORDER BY sensor_name ASC;";

            ReadDistinctColumn(sql, filterDevice ? deviceId : null, list);
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

        public static List<string> GetDistinctCpuCores()
        {
            List<string> list = new List<string>();
            ReadDistinctColumn("SELECT DISTINCT core_name FROM cpu_clock_logs;", null, list);
            list.Sort(NaturalStringComparer.Instance);
            return list;
        }

        public static List<CpuCompositeLogEntry> ReadCpuCompositeHistory(DateTime? startTime, DateTime? endTime)
        {
            Dictionary<string, CpuCompositeLogEntry> map = new Dictionary<string, CpuCompositeLogEntry>();

            lock (_dbLock)
            {
                IntPtr db;
                string dbPath = GetDatabasePath();
                if (!File.Exists(dbPath)) return new List<CpuCompositeLogEntry>();
                if (sqlite3_open16(dbPath, out db) != SQLITE_OK) return new List<CpuCompositeLogEntry>();

                try
                {
                    // 1. CPU 温度ログの取得 (device_id = 'CPU' かつ is_device_max = 1)
                    StringBuilder sqlTemp = new StringBuilder();
                    sqlTemp.Append("SELECT timestamp, temperature FROM sensor_logs ");
                    sqlTemp.Append("WHERE device_id = 'CPU' AND is_device_max = 1 ");
                    AppendTimeRangeClause(sqlTemp, startTime, endTime);
                    sqlTemp.Append("ORDER BY timestamp ASC;");

                    IntPtr stmtTemp = IntPtr.Zero;
                    if (sqlite3_prepare16_v2(db, sqlTemp.ToString(), -1, out stmtTemp, IntPtr.Zero) == SQLITE_OK)
                    {
                        try
                        {
                            int bindIdx = 1;
                            BindTimeRange(stmtTemp, ref bindIdx, startTime, endTime);

                            while (sqlite3_step(stmtTemp) == SQLITE_ROW)
                            {
                                string timeStr = GetColumnText(stmtTemp, 0);
                                float temp = (float)sqlite3_column_double(stmtTemp, 1);

                                CpuCompositeLogEntry entry;
                                if (!map.TryGetValue(timeStr, out entry))
                                {
                                    entry = new CpuCompositeLogEntry { Timestamp = ParseTimestamp(timeStr) };
                                    map[timeStr] = entry;
                                }
                                entry.CpuTemperature = temp;
                            }
                        }
                        finally
                        {
                            sqlite3_finalize(stmtTemp);
                        }
                    }

                    // 2. CPU クロックログの取得
                    StringBuilder sqlClock = new StringBuilder();
                    sqlClock.Append("SELECT timestamp, core_name, clock_mhz FROM cpu_clock_logs ");
                    sqlClock.Append("WHERE 1=1 ");
                    AppendTimeRangeClause(sqlClock, startTime, endTime);
                    sqlClock.Append("ORDER BY timestamp ASC, core_name ASC;");

                    IntPtr stmtClock = IntPtr.Zero;
                    if (sqlite3_prepare16_v2(db, sqlClock.ToString(), -1, out stmtClock, IntPtr.Zero) == SQLITE_OK)
                    {
                        try
                        {
                            int bindIdx = 1;
                            BindTimeRange(stmtClock, ref bindIdx, startTime, endTime);

                            while (sqlite3_step(stmtClock) == SQLITE_ROW)
                            {
                                string timeStr = GetColumnText(stmtClock, 0);
                                string core = GetColumnText(stmtClock, 1);
                                float mhz = (float)sqlite3_column_double(stmtClock, 2);

                                CpuCompositeLogEntry entry;
                                if (!map.TryGetValue(timeStr, out entry))
                                {
                                    entry = new CpuCompositeLogEntry { Timestamp = ParseTimestamp(timeStr) };
                                    map[timeStr] = entry;
                                }
                                entry.CoreClocks[core] = mhz;
                            }
                        }
                        finally
                        {
                            sqlite3_finalize(stmtClock);
                        }
                    }
                }
                finally
                {
                    sqlite3_close(db);
                }
            }

            // 各エントリーの MaxClock / AvgClock の集約
            List<CpuCompositeLogEntry> result = new List<CpuCompositeLogEntry>(map.Values);
            for (int i = 0; i < result.Count; i++)
            {
                CpuCompositeLogEntry e = result[i];
                if (e.CoreClocks.Count > 0)
                {
                    float max = 0f;
                    float sum = 0f;
                    foreach (KeyValuePair<string, float> kvp in e.CoreClocks)
                    {
                        if (kvp.Value > max) max = kvp.Value;
                        sum += kvp.Value;
                    }
                    e.MaxClock = max;
                    e.AvgClock = sum / e.CoreClocks.Count;
                }
            }

            result.Sort(delegate(CpuCompositeLogEntry a, CpuCompositeLogEntry b)
            {
                return a.Timestamp.CompareTo(b.Timestamp);
            });

            return result;
        }

        private static void ReadDistinctColumn(string sql, string optionalParam, List<string> targetList)
        {
            lock (_dbLock)
            {
                IntPtr db;
                string dbPath = GetDatabasePath();
                if (!File.Exists(dbPath)) return;
                if (sqlite3_open16(dbPath, out db) != SQLITE_OK) return;

                IntPtr stmt = IntPtr.Zero;
                try
                {
                    if (sqlite3_prepare16_v2(db, sql, -1, out stmt, IntPtr.Zero) == SQLITE_OK)
                    {
                        if (optionalParam != null)
                        {
                            sqlite3_bind_text16(stmt, 1, optionalParam, -1, IntPtr.Zero);
                        }

                        while (sqlite3_step(stmt) == SQLITE_ROW)
                        {
                            string val = GetColumnText(stmt, 0);
                            if (!string.IsNullOrEmpty(val) && !targetList.Contains(val))
                            {
                                targetList.Add(val);
                            }
                        }
                    }
                }
                finally
                {
                    if (stmt != IntPtr.Zero) sqlite3_finalize(stmt);
                    sqlite3_close(db);
                }
            }
        }

        private static void AppendTimeRangeClause(StringBuilder sql, DateTime? startTime, DateTime? endTime)
        {
            if (startTime.HasValue) sql.Append("AND timestamp >= ? ");
            if (endTime.HasValue) sql.Append("AND timestamp <= ? ");
        }

        private static void BindTimeRange(IntPtr stmt, ref int bindIdx, DateTime? startTime, DateTime? endTime)
        {
            if (startTime.HasValue)
            {
                sqlite3_bind_text16(stmt, bindIdx++, startTime.Value.ToString(TimestampFormat), -1, IntPtr.Zero);
            }
            if (endTime.HasValue)
            {
                sqlite3_bind_text16(stmt, bindIdx++, endTime.Value.ToString(TimestampFormat), -1, IntPtr.Zero);
            }
        }

        private static string GetColumnText(IntPtr stmt, int col)
        {
            IntPtr ptr = sqlite3_column_text16(stmt, col);
            return ptr != IntPtr.Zero ? Marshal.PtrToStringUni(ptr) : string.Empty;
        }

        private static DateTime ParseTimestamp(string timeStr)
        {
            DateTime dt;
            if (!string.IsNullOrEmpty(timeStr) &&
                DateTime.TryParseExact(timeStr, TimestampFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
            {
                return dt;
            }
            return DateTime.Now;
        }

        private static string EscapeCsv(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Replace("\"", "\"\"");
        }
    }
}
