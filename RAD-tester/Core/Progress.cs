// Progress.cs - プロトコル進行状況 (9 フェーズ) のトラッキングとサマリ表示

using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace RadiusTest
{
    // ------------------------------------------------------------------
    // プロトコル進行状況トラッキング
    // ------------------------------------------------------------------
    public enum PState { Ok, Warn, Fail, Skip, Info }

    public class ProgressEntry
    {
        public string Phase = "";
        public PState State;
        public string Msg = "";
        public long ElapsedMs;
        public string Flow = ""; // 通信方向 (例: Supplicant --> RADIUS, Authenticator <-- RADIUS)
    }

    public static class Progress
    {
        private static readonly Stopwatch Sw = new Stopwatch();
        public static readonly List<ProgressEntry> Entries = new List<ProgressEntry>();
        public static event Action<ProgressEntry>? OnProgress;

        public static void Reset()
        {
            Entries.Clear();
            Sw.Restart();
        }

        public static void Log(string phase, PState state, string msg, string flow = "")
        {
            long ms = Sw.ElapsedMilliseconds;
            var entry = new ProgressEntry { Phase = phase, State = state, Msg = msg, ElapsedMs = ms, Flow = flow };
            Entries.Add(entry);
            try { OnProgress?.Invoke(entry); } catch { }

            ConsoleColor sc = state switch
            {
                PState.Ok => ConsoleColor.Green,
                PState.Warn => ConsoleColor.Yellow,
                PState.Fail => ConsoleColor.Red,
                PState.Skip => ConsoleColor.DarkGray,
                _ => ConsoleColor.Cyan
            };
            string tag = state switch
            {
                PState.Ok => " OK ",
                PState.Warn => "WARN",
                PState.Fail => "FAIL",
                PState.Skip => " SKIP",
                _ => " .. "
            };

            var segs = new List<Seg>
            {
                new Seg(ConsoleColor.DarkGray, $"[{ms,6} ms] "),
                new Seg(ConsoleColor.White, $"{phase,-8} "),
                new Seg(sc, $"[{tag}] ")
            };
            if (!string.IsNullOrEmpty(flow))
            {
                segs.Add(new Seg(ConsoleColor.Cyan, $"[{flow,-22}] "));
            }
            segs.Add(new Seg(ConsoleColor.Gray, msg));

            Out.Segs(false, segs.ToArray());
        }

        public static void Info(string phase, string msg)
        {
            Out.Segs(false,
                new Seg(ConsoleColor.DarkGray, "          "),
                new Seg(ConsoleColor.DarkGray, $"{phase,-8} "),
                new Seg(ConsoleColor.DarkGray, "       "),
                new Seg(ConsoleColor.DarkGray, msg)
            );
        }

        public static void Skip(string phase, string reason, string flow = "")
        {
            var entry = new ProgressEntry { Phase = phase, State = PState.Skip, Msg = reason, ElapsedMs = Sw.ElapsedMilliseconds, Flow = flow };
            Entries.Add(entry);
            try { OnProgress?.Invoke(entry); } catch { }
        }

        public static void PrintSummary()
        {
            string[] allPhases = { "INIT", "RESOLVE", "CONNECT", "TLS", "BUILD", "SEND", "RECV", "VERIFY", "RESULT" };
            Out.Blank(false);
            Out.Line(false, ConsoleColor.Cyan, "================ プロトコル進行状況 ================");

            int completed = 0;
            string? stoppedAt = null;

            for (int i = 0; i < allPhases.Length; i++)
            {
                string p = allPhases[i];
                ProgressEntry? match = null;
                for (int j = Entries.Count - 1; j >= 0; j--)
                {
                    if (Entries[j].Phase == p) { match = Entries[j]; break; }
                }

                if (match == null)
                {
                    Out.Segs(false,
                        new Seg(ConsoleColor.DarkGray, $" {i + 1,2}. {p,-8} "),
                        new Seg(ConsoleColor.DarkGray, "[ -- ]（未到達）")
                    );
                    if (stoppedAt == null) stoppedAt = p;
                }
                else if (match.State == PState.Skip)
                {
                    Out.Segs(false,
                        new Seg(ConsoleColor.DarkGray, $" {i + 1,2}. {p,-8} "),
                        new Seg(ConsoleColor.DarkGray, "[N/A ] "),
                        new Seg(ConsoleColor.DarkGray, match.Msg)
                    );
                    completed++;
                }
                else
                {
                    ConsoleColor c = match.State == PState.Ok ? ConsoleColor.Green :
                                     match.State == PState.Warn ? ConsoleColor.Yellow : ConsoleColor.Red;
                    string tag = match.State == PState.Ok ? " OK " :
                                 match.State == PState.Warn ? "WARN" : "FAIL";
                    var sumSegs = new List<Seg>
                    {
                        new Seg(ConsoleColor.White, $" {i + 1,2}. {p,-8} "),
                        new Seg(c, $"[{tag}] ")
                    };
                    if (!string.IsNullOrEmpty(match.Flow))
                    {
                        sumSegs.Add(new Seg(ConsoleColor.Cyan, $"[{match.Flow,-22}] "));
                    }
                    sumSegs.Add(new Seg(ConsoleColor.Gray, match.Msg));
                    Out.Segs(false, sumSegs.ToArray());
                    if (match.State == PState.Ok || match.State == PState.Warn) completed++;
                    else if (stoppedAt == null) stoppedAt = p;
                }
            }

            if (completed == allPhases.Length)
            {
                Out.Line(false, ConsoleColor.Green, " 進行状況: 全 9 フェーズが完了しました。");
            }
            else
            {
                Out.Line(false, ConsoleColor.Yellow, $" 進行状況: {completed}/9 フェーズ完了、{stoppedAt} で停止しました。");
            }
            Out.Line(false, ConsoleColor.Cyan, "===================================================");
        }
    }
}
