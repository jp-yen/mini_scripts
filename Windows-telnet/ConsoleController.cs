using System.Runtime.InteropServices;
using System.Text;

namespace Windows_telnet;

/// <summary>
/// Windows Terminal / ConPTY の Raw モード設定、VT 入出力有効化、
/// およびコンソールウィンドウサイズ監視を行うコントローラー
/// </summary>
public sealed class ConsoleController : IDisposable
{
    private const int STD_INPUT_HANDLE = -10;
    private const int STD_OUTPUT_HANDLE = -11;

    // Input flags
    private const uint ENABLE_PROCESSED_INPUT = 0x0001;
    private const uint ENABLE_LINE_INPUT = 0x0002;
    private const uint ENABLE_ECHO_INPUT = 0x0004;
    private const uint ENABLE_WINDOW_INPUT = 0x0008;
    private const uint ENABLE_MOUSE_INPUT = 0x0010;
    private const uint ENABLE_VIRTUAL_TERMINAL_INPUT = 0x0200;

    // Output flags
    private const uint ENABLE_PROCESSED_OUTPUT = 0x0001;
    private const uint ENABLE_WRAP_AT_EOL_OUTPUT = 0x0002;
    private const uint ENABLE_VIRTUAL_TERMINAL_PROCESSING = 0x0004;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetConsoleMode(nint hConsoleHandle, out uint lpMode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleMode(nint hConsoleHandle, uint dwMode);

    private readonly nint _inHandle;
    private readonly nint _outHandle;
    private readonly bool _hasInputConsole;
    private readonly bool _hasOutputConsole;
    private readonly uint _originalInMode;
    private readonly uint _originalOutMode;
    private readonly Encoding? _originalInEncoding;
    private readonly Encoding? _originalOutEncoding;

    private readonly CancellationTokenSource _cts = new();
    private Task? _resizeWatcherTask;

    public event Action<int, int>? WindowResized;

    public int CurrentWidth
    {
        get
        {
            try
            {
                return Math.Max(1, Console.WindowWidth);
            }
            catch
            {
                return 80;
            }
        }
    }

    public int CurrentHeight
    {
        get
        {
            try
            {
                return Math.Max(1, Console.WindowHeight);
            }
            catch
            {
                return 24;
            }
        }
    }

    public ConsoleController()
    {
        _inHandle = GetStdHandle(STD_INPUT_HANDLE);
        _outHandle = GetStdHandle(STD_OUTPUT_HANDLE);

        try
        {
            _originalInEncoding = Console.InputEncoding;
            _originalOutEncoding = Console.OutputEncoding;
            Console.InputEncoding = Encoding.UTF8;
            Console.OutputEncoding = Encoding.UTF8;
        }
        catch { }

        // コンソールハンドルの有効性を確認
        _hasInputConsole = GetConsoleMode(_inHandle, out _originalInMode);
        _hasOutputConsole = GetConsoleMode(_outHandle, out _originalOutMode);

        try
        {
            if (!Console.IsInputRedirected)
            {
                Console.TreatControlCAsInput = true;
            }
        }
        catch { }
    }

    /// <summary>
    /// Raw モードおよび Virtual Terminal Processing を有効化
    /// </summary>
    public void EnableRawMode()
    {
        if (_hasOutputConsole)
        {
            // 出力: VT100 / xterm シーケンスを解釈可能にする
            uint outMode = _originalOutMode | ENABLE_PROCESSED_OUTPUT | ENABLE_WRAP_AT_EOL_OUTPUT | ENABLE_VIRTUAL_TERMINAL_PROCESSING;
            SetConsoleMode(_outHandle, outMode);
        }

        if (_hasInputConsole)
        {
            // 入力: 行入力・エコー・シグナル処理・余計なイベント入力をOFFにし、VT入力を有効化
            uint inMode = _originalInMode;
            inMode &= ~ENABLE_LINE_INPUT;
            inMode &= ~ENABLE_ECHO_INPUT;
            inMode &= ~ENABLE_PROCESSED_INPUT;
            inMode &= ~ENABLE_WINDOW_INPUT;
            inMode &= ~ENABLE_MOUSE_INPUT;
            inMode |= ENABLE_VIRTUAL_TERMINAL_INPUT;
            SetConsoleMode(_inHandle, inMode);
        }

        // ウィンドウリサイズ監視を開始
        StartResizeWatcher();
    }

    /// <summary>
    /// ウィンドウサイズ変更を監視するバックグラウンドタスク
    /// </summary>
    private void StartResizeWatcher()
    {
        _resizeWatcherTask = Task.Run(async () =>
        {
            int lastW = CurrentWidth;
            int lastH = CurrentHeight;

            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(200, _cts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                int currW = CurrentWidth;
                int currH = CurrentHeight;

                if (currW != lastW || currH != lastH)
                {
                    lastW = currW;
                    lastH = currH;
                    WindowResized?.Invoke(currW, currH);
                }
            }
        });
    }

    /// <summary>
    /// 元のコンソールモードとエンコーディングを復元
    /// </summary>
    public void Restore()
    {
        try
        {
            _cts.Cancel();
            _resizeWatcherTask?.Wait(300);
        }
        catch { }

        try
        {
            if (!Console.IsInputRedirected)
            {
                Console.TreatControlCAsInput = false;
            }
        }
        catch { }

        if (_hasInputConsole && _inHandle != nint.Zero)
        {
            SetConsoleMode(_inHandle, _originalInMode);
        }
        if (_hasOutputConsole && _outHandle != nint.Zero)
        {
            SetConsoleMode(_outHandle, _originalOutMode);
        }

        try
        {
            if (_originalInEncoding != null) Console.InputEncoding = _originalInEncoding;
            if (_originalOutEncoding != null) Console.OutputEncoding = _originalOutEncoding;
        }
        catch { }
    }

    private bool _disposed;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Restore();
        _cts.Dispose();
    }
}
