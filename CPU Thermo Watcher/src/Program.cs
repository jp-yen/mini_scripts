// --------------------------------------------------------------------------------------------------------------------
// <copyright file="Program.cs" company="CPU Thermo Watcher Contributors">
//   Copyright (c) 2026, CPU Thermo Watcher Contributors. All rights reserved.
//   Licensed under the 3-Clause BSD License.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Windows.Forms;

namespace CpuThermoWatcher
{
    static class Program
    {
        private const string MutexName = "Global\\CpuThermoWatcher_SingleInstanceMutex";

        [STAThread]
        static void Main()
        {
            bool createdNew;
            using (Mutex mutex = new Mutex(true, MutexName, out createdNew))
            {
                if (!createdNew)
                {
                    MessageBox.Show(
                        "CPU Thermo Watcher は既に起動しています。\nタスクトレイ（通知領域）のアイコンをご確認ください。",
                        "二重起動の防止",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                // 設定の読み込み
                Settings settings = Settings.Load();

                // ハードウェアモニタとロガーの初期化
                using (HardwareMonitor hwMonitor = new HardwareMonitor())
                {
                    EventLogger eventLogger = new EventLogger();

                    using (TrayIconManager trayManager = new TrayIconManager(settings, hwMonitor, eventLogger))
                    {
                        Application.Run();
                    }
                }
            }
        }
    }
}
