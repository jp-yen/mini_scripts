// Output.cs - 出力シンク (CLI コンソール / GUI RichTextBox 切替用)

using System;

namespace RadiusTest
{
    // ------------------------------------------------------------------
    // 出力シンク (CLI コンソール / 将来の GUI RichTextBox / イベント連携用)
    // ------------------------------------------------------------------
    public struct Seg
    {
        public ConsoleColor C;
        public string T;
        public Seg(ConsoleColor c, string t) { C = c; T = t; }
    }

    public interface IOutput
    {
        void Write(bool detail, Seg[] line);
    }

    public class ConsoleOutput : IOutput
    {
        public void Write(bool detail, Seg[] line)
        {
            foreach (Seg s in line)
            {
                Console.ForegroundColor = s.C;
                Console.Write(s.T);
            }
            Console.ResetColor();
            Console.WriteLine();
        }
    }

    public static class Out
    {
        public static IOutput Sink = new ConsoleOutput();
        public static Action<string>? Prompt;
        public static Func<string, string?>? PromptFunc;

        public static void Line(bool detail, ConsoleColor c, string text)
        {
            Sink.Write(detail, new[] { new Seg(c, text) });
        }

        public static void Blank(bool detail)
        {
            Sink.Write(detail, new[] { new Seg(ConsoleColor.Gray, "") });
        }

        public static void Segs(bool detail, params Seg[] segs)
        {
            Sink.Write(detail, segs);
        }
    }
}
