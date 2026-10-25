using System.Net.Sockets;
using System.Reflection;

namespace Windows_telnet;

internal class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args.Contains("-h") || args.Contains("--help") || args.Contains("-?"))
        {
            ShowHelp();
            return 0;
        }

        string? host = null;
        int port = 23;
        string terminalType = "xterm-256color";
        byte escapeChar = TelnetConstants.DEFAULT_ESCAPE_CHAR; // 0x1D (Ctrl+])

        // 引数解析
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];

            if (arg is "-p" or "--port")
            {
                if (i + 1 < args.Length && int.TryParse(args[++i], out int p) && p is >= 1 and <= 65535)
                {
                    port = p;
                }
                else
                {
                    Console.Error.WriteLine("Error: Port number must be an integer between 1 and 65535.");
                    return 1;
                }
            }
            else if (arg is "-t" or "--term")
            {
                if (i + 1 < args.Length && !string.IsNullOrWhiteSpace(args[i + 1]))
                {
                    terminalType = args[++i];
                }
                else
                {
                    Console.Error.WriteLine("Error: Terminal type is missing or invalid.");
                    return 1;
                }
            }
            else if (arg is "-e" or "--escape")
            {
                if (i + 1 < args.Length)
                {
                    string esc = args[++i];
                    if (esc.StartsWith('^') && esc.Length == 2)
                    {
                        // ^] 形式
                        escapeChar = (byte)(char.ToUpperInvariant(esc[1]) - '@');
                    }
                    else if (esc.Length > 0)
                    {
                        escapeChar = (byte)esc[0];
                    }
                }
            }
            else if (!arg.StartsWith('-'))
            {
                if (host == null)
                {
                    host = arg;
                }
                else if (int.TryParse(arg, out int p) && p is >= 1 and <= 65535)
                {
                    port = p;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(host))
        {
            Console.Error.WriteLine("Error: Target host or IP address is required.");
            ShowHelp();
            return 1;
        }

        using var console = new ConsoleController();

        // プロセス終了時（SIGINT/Ctrl+Cなど）のコンソール復元保証
        AppDomain.CurrentDomain.ProcessExit += (_, _) => console.Restore();
        Console.CancelKeyPress += (_, e) =>
        {
            // コンソール側でシグナル終了させず、サーバーへ透過
            e.Cancel = true;
        };

        try
        {
            using var client = new TelnetClient(host, port, terminalType, escapeChar, console);
            await client.RunAsync();
            return 0;
        }
        catch (SocketException ex)
        {
            console.Restore();
            Console.Error.WriteLine($"\r\nConnection error: {ex.Message} ({host}:{port})");
            return 2;
        }
        catch (Exception ex)
        {
            console.Restore();
            Console.Error.WriteLine($"\r\nUnexpected error: {ex.Message}");
            return 3;
        }
        finally
        {
            console.Restore();
        }
    }

    private static void ShowHelp()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
        Console.WriteLine($"Windows-telnet v{version} (.NET 10 / Windows Terminal Optimized)");
        Console.WriteLine("Modern Telnet client with RFC-compliant ANSI/VT, NAWS, and UTF-8 support.");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  telnet <host> [port] [options]");
        Console.WriteLine();
        Console.WriteLine("Arguments:");
        Console.WriteLine("  <host>              Target hostname or IP address (required)");
        Console.WriteLine("  [port]              Target port number 1-65535 (default: 23)");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  -p, --port <port>   Target port number (default: 23)");
        Console.WriteLine("  -t, --term <type>   Terminal type to negotiate (default: xterm-256color)");
        Console.WriteLine("  -e, --escape <char> Escape character (default: ^] = Ctrl+])");
        Console.WriteLine("  -h, -?, --help      Display this help message");
        Console.WriteLine();
        Console.WriteLine("Features:");
        Console.WriteLine("  * Windows Terminal / ConPTY full VT processing & raw input");
        Console.WriteLine("  * NAWS (RFC 1073) dynamic window resize notifications (SIGWINCH)");
        Console.WriteLine("  * UTF-8 Unicode pass-through (RFC 856 8-bit binary)");
        Console.WriteLine("  * Escape prompt (Ctrl+] -> Win_tel>) for status inspection or disconnection");
    }
}
