// --------------------------------------------------------------------------------------------------------------------
// <copyright file="UiHelper.cs" company="CPU Thermo Watcher Contributors">
//   Copyright (c) 2026, CPU Thermo Watcher Contributors. All rights reserved.
//   Licensed under the 3-Clause BSD License.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;

namespace CpuThermoWatcher
{
    internal sealed class NaturalStringComparer : IComparer<string>
    {
        public static readonly NaturalStringComparer Instance = new NaturalStringComparer();

        public int Compare(string a, string b)
        {
            return CompareNatural(a, b);
        }

        public static int CompareNatural(string a, string b)
        {
            if (ReferenceEquals(a, b)) return 0;
            if (a == null) return -1;
            if (b == null) return 1;

            int ia = 0;
            int ib = 0;
            while (ia < a.Length && ib < b.Length)
            {
                if (char.IsDigit(a[ia]) && char.IsDigit(b[ib]))
                {
                    // 先頭のゼロを読み飛ばしつつ数値部の範囲を取得
                    int zeroStartA = ia;
                    while (ia < a.Length && a[ia] == '0') ia++;
                    int digitStartA = ia;
                    while (ia < a.Length && char.IsDigit(a[ia])) ia++;
                    int sigLenA = ia - digitStartA;

                    int zeroStartB = ib;
                    while (ib < b.Length && b[ib] == '0') ib++;
                    int digitStartB = ib;
                    while (ib < b.Length && char.IsDigit(b[ib])) ib++;
                    int sigLenB = ib - digitStartB;

                    // 有効桁数が異なれば桁数が多い方が大きい（オーバーフロー完全回避）
                    if (sigLenA != sigLenB)
                    {
                        return sigLenA.CompareTo(sigLenB);
                    }

                    // 同じ桁数なら先頭から文字比較
                    for (int k = 0; k < sigLenA; k++)
                    {
                        int dc = a[digitStartA + k].CompareTo(b[digitStartB + k]);
                        if (dc != 0) return dc;
                    }

                    // 数値として等しい場合（例: "01" と "1"）は全体の文字数で比較
                    int totalLenA = ia - zeroStartA;
                    int totalLenB = ib - zeroStartB;
                    if (totalLenA != totalLenB)
                    {
                        return totalLenA.CompareTo(totalLenB);
                    }
                }
                else
                {
                    int c = char.ToUpperInvariant(a[ia]).CompareTo(char.ToUpperInvariant(b[ib]));
                    if (c != 0) return c;
                    ia++;
                    ib++;
                }
            }

            return (a.Length - ia).CompareTo(b.Length - ib);
        }
    }

    internal static class UiHelper
    {
        public const int CustomRangeIndex = 5;

        public static readonly object[] TimeRangePresets = new object[]
        {
            "直近 1 時間",
            "直近 6 時間",
            "直近 24 時間",
            "直近 7 日間",
            "全期間",
            "カスタム指定..."
        };

        public static float GetDpiScale(Control control)
        {
            float dpiScale = 1.0f;
            if (control != null)
            {
                using (Graphics g = control.CreateGraphics())
                {
                    dpiScale = g.DpiY / 96.0f;
                }
            }
            return dpiScale < 1.0f ? 1.0f : dpiScale;
        }

        public static void EnableDoubleBuffering(Control control)
        {
            if (control == null) return;
            try
            {
                PropertyInfo prop = typeof(Control).GetProperty("DoubleBuffered", BindingFlags.NonPublic | BindingFlags.Instance);
                if (prop != null)
                {
                    prop.SetValue(control, true, null);
                }
            }
            catch
            {
            }
        }

        public static string FormatDeviceLabel(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId)) return "CPU";
            return deviceId.Replace(":", " #");
        }

        public static void ResolveTimeRange(
            int selectedIndex,
            DateTime customStart,
            DateTime customEnd,
            out DateTime? startTime,
            out DateTime? endTime)
        {
            startTime = null;
            endTime = null;
            DateTime now = DateTime.Now;

            switch (selectedIndex)
            {
                case 0: startTime = now.AddHours(-1); break;
                case 1: startTime = now.AddHours(-6); break;
                case 2: startTime = now.AddHours(-24); break;
                case 3: startTime = now.AddDays(-7); break;
                case 4: startTime = null; endTime = null; break; // 全期間
                case CustomRangeIndex:
                    startTime = customStart;
                    endTime = customEnd;
                    break;
            }
        }

        public static void UpdateChartTimeAxisFormat(Axis axisX, DateTime? firstTimestamp, DateTime? lastTimestamp, int rangeIndex)
        {
            if (axisX == null) return;

            double spanHours = 1.0;
            if (firstTimestamp.HasValue && lastTimestamp.HasValue)
            {
                spanHours = (lastTimestamp.Value - firstTimestamp.Value).TotalHours;
            }
            else if (rangeIndex >= 2)
            {
                spanHours = 25.0;
            }

            axisX.LabelStyle.Format = spanHours >= 24.0 ? "MM/dd HH:mm" : "HH:mm";
        }

        public static void ShowChartTooltip(ToolTip toolTip, Control chart, Point mousePos, string text, float dpiScale)
        {
            if (toolTip == null || chart == null) return;

            int offset = (int)(14 * dpiScale);
            int boxWidth = (int)(190 * dpiScale);
            int boxHeight = (int)(70 * dpiScale);

            int tipX = mousePos.X + offset;
            int tipY = mousePos.Y + offset;

            if (tipX + boxWidth > chart.Width)
            {
                tipX = Math.Max(0, mousePos.X - boxWidth);
            }
            if (tipY + boxHeight > chart.Height)
            {
                tipY = Math.Max(0, mousePos.Y - boxHeight);
            }

            toolTip.Show(text, chart, tipX, tipY);
        }
    }
}

