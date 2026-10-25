using System.Net.Sockets;
using System.Text;

namespace Windows_telnet;

/// <summary>
/// RFC 854/856/857/858/1073/1091 に準拠したモダン Telnet クライアント
/// </summary>
public sealed class TelnetClient : IDisposable
{
    private const int MaxSubnegotiationLength = 2048; // メモリ枯渇DoS防止用のサブネゴシエーション上限サイズ

    private enum ParserState
    {
        Data,
        Iac,
        Will,
        Wont,
        Do,
        Dont,
        SbData,
        SbIac
    }

    private readonly string _host;
    private readonly int _port;
    private readonly string _terminalType;
    private readonly byte _escapeChar;
    private readonly ConsoleController _console;

    private TcpClient? _tcpClient;
    private NetworkStream? _stream;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly CancellationTokenSource _cts = new();

    // ネゴシエーション状態管理（ループ防止用）
    private readonly HashSet<byte> _myWills = [];
    private readonly HashSet<byte> _myDos   = [];

    private bool _nawsEnabled;
    private bool _remoteEcho;
    private bool _disposed;

    public TelnetClient(string host, int port, string terminalType, byte escapeChar, ConsoleController console)
    {
        _host = host;
        _port = port;

        // 端末タイプのサニタイズ (制御文字やエスケープシーケンスの混入防止)
        _terminalType = SanitizeTerminalType(terminalType);
        _escapeChar = escapeChar;
        _console = console;

        _console.WindowResized += OnWindowResized;
    }

    private static string SanitizeTerminalType(string term)
    {
        if (string.IsNullOrWhiteSpace(term)) return "xterm-256color";

        // ASCII印字可能文字（英数字、ハイフン、アンダースコア）のみに限定
        var sb = new StringBuilder();
        foreach (char c in term)
        {
            if (char.IsLetterOrDigit(c) || c is '-' or '_' or '.')
            {
                sb.Append(c);
            }
        }
        return sb.Length > 0 ? sb.ToString() : "xterm-256color";
    }

    /// <summary>
    /// 接続し、送受信ループを開始
    /// </summary>
    public async Task RunAsync()
    {
        _tcpClient = new TcpClient
        {
            NoDelay = true // 低遅延通信
        };

        Console.WriteLine($"Trying {_host} (port {_port})...");
        await _tcpClient.ConnectAsync(_host, _port, _cts.Token);
        Console.WriteLine($"Connected to {_host}.");
        Console.WriteLine($"Escape character is 'Ctrl+]'. Type 'Ctrl+]' to enter command prompt.");

        _stream = _tcpClient.GetStream();

        // 接続直後: クライアント側の対応能力 (NAWS, TTYPE) のみをサーバーに通知
        // ※ BINARY や SGA、ECHO はサーバー主導の折衝に委ねることで login/telnetd との不整合を防ぐ
        await SendWillAsync(TelnetConstants.OPT_NAWS);
        await SendWillAsync(TelnetConstants.OPT_TERMINAL_TYPE);

        // コンソールを Raw モードに設定
        _console.EnableRawMode();

        var receiveTask = Task.Run(ReceiveLoopAsync);
        var sendTask = Task.Run(SendLoopAsync);

        await Task.WhenAny(receiveTask, sendTask);

        // いずれかが終了したらキャンセル
        _cts.Cancel();
        try
        {
            await Task.WhenAll(receiveTask, sendTask);
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>
    /// サーバーからの受信および Telnet パーサー処理ループ
    /// </summary>
    private async Task ReceiveLoopAsync()
    {
        var parserState = ParserState.Data;
        var sbOption = (byte)0;
        var sbBuffer = new List<byte>();
        var buffer = new byte[8192];
        var outStream = Console.OpenStandardOutput();

        try
        {
            while (!_cts.IsCancellationRequested && _stream != null)
            {
                int bytesRead = await _stream.ReadAsync(buffer, 0, buffer.Length, _cts.Token);
                if (bytesRead <= 0)
                {
                    // サーバー側切断
                    break;
                }

                var outBuffer = new List<byte>(bytesRead);

                for (int i = 0; i < bytesRead; i++)
                {
                    byte b = buffer[i];

                    switch (parserState)
                    {
                        case ParserState.Data:
                            if (b == TelnetConstants.IAC)
                            {
                                parserState = ParserState.Iac;
                            }
                            else
                            {
                                outBuffer.Add(b);
                            }
                            break;

                        case ParserState.Iac:
                            switch (b)
                            {
                                case TelnetConstants.IAC:
                                    // 255 のエスケープ (IAC IAC -> 255)
                                    outBuffer.Add(TelnetConstants.IAC);
                                    parserState = ParserState.Data;
                                    break;
                                case TelnetConstants.WILL:
                                    parserState = ParserState.Will;
                                    break;
                                case TelnetConstants.WONT:
                                    parserState = ParserState.Wont;
                                    break;
                                case TelnetConstants.DO:
                                    parserState = ParserState.Do;
                                    break;
                                case TelnetConstants.DONT:
                                    parserState = ParserState.Dont;
                                    break;
                                case TelnetConstants.SB:
                                    sbBuffer.Clear();
                                    sbOption = 0;
                                    parserState = ParserState.SbData;
                                    break;
                                default:
                                    // NOP, GA, etc.
                                    parserState = ParserState.Data;
                                    break;
                            }
                            break;

                        case ParserState.Will:
                            await HandleWillAsync(b);
                            parserState = ParserState.Data;
                            break;

                        case ParserState.Wont:
                            await HandleWontAsync(b);
                            parserState = ParserState.Data;
                            break;

                        case ParserState.Do:
                            await HandleDoAsync(b);
                            parserState = ParserState.Data;
                            break;

                        case ParserState.Dont:
                            await HandleDontAsync(b);
                            parserState = ParserState.Data;
                            break;

                        case ParserState.SbData:
                            if (sbOption == 0)
                            {
                                sbOption = b;
                            }
                            else if (b == TelnetConstants.IAC)
                            {
                                parserState = ParserState.SbIac;
                            }
                            else
                            {
                                // バッファサイズ上限チェック (メモリ枯渇 DoS 防止)
                                if (sbBuffer.Count < MaxSubnegotiationLength)
                                {
                                    sbBuffer.Add(b);
                                }
                                else
                                {
                                    // 異常に長いサブネゴシエーションは破棄して Data 状態へ復帰
                                    parserState = ParserState.Data;
                                    sbBuffer.Clear();
                                }
                            }
                            break;

                        case ParserState.SbIac:
                            if (b == TelnetConstants.SE)
                            {
                                await HandleSubnegotiationAsync(sbOption, sbBuffer.ToArray());
                                parserState = ParserState.Data;
                            }
                            else if (b == TelnetConstants.IAC)
                            {
                                // クォートされた 255
                                if (sbBuffer.Count < MaxSubnegotiationLength)
                                {
                                    sbBuffer.Add(TelnetConstants.IAC);
                                    parserState = ParserState.SbData;
                                }
                                else
                                {
                                    parserState = ParserState.Data;
                                    sbBuffer.Clear();
                                }
                            }
                            else
                            {
                                parserState = ParserState.Data;
                            }
                            break;
                    }
                }

                if (outBuffer.Count > 0)
                {
                    await outStream.WriteAsync(outBuffer.ToArray(), 0, outBuffer.Count, _cts.Token);
                    await outStream.FlushAsync(_cts.Token);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (!_cts.IsCancellationRequested)
        {
            Console.WriteLine($"\r\n[Receive error: {ex.Message}]");
        }
    }

    /// <summary>
    /// 標準入力（キーボード）からの送信処理ループ
    /// </summary>
    private async Task SendLoopAsync()
    {
        var inStream = Console.OpenStandardInput();
        var buffer = new byte[1024];

        try
        {
            while (!_cts.IsCancellationRequested && _stream != null)
            {
                int bytesRead = await inStream.ReadAsync(buffer, 0, buffer.Length, _cts.Token);
                if (bytesRead <= 0)
                {
                    break;
                }

                var sendBuffer = new List<byte>(bytesRead * 2);

                for (int i = 0; i < bytesRead; i++)
                {
                    byte b = buffer[i];

                    // エスケープ文字（Ctrl+]）の検出
                    if (b == _escapeChar)
                    {
                        if (sendBuffer.Count > 0)
                        {
                            await SendBytesAsync(sendBuffer.ToArray());
                            sendBuffer.Clear();
                        }

                        bool continueSession = await HandleEscapePromptAsync();
                        if (!continueSession)
                        {
                            _cts.Cancel();
                            return;
                        }
                        continue;
                    }

                    // 改行コード（Enterキー）の処理:
                    // Linux/bash (BINARY/SGAモード) では PTY が ICRNL を処理するため、
                    // Enter は CR (0x0D) 単体を送信する。
                    // (CR LF を送ると CR で 1 回、LF で 1 回の計 2 回改行されてプロンプトが 2 行出る原因になる)
                    if (b == 0x0D) // CR
                    {
                        // 直後に LF (0x0A) が連続している場合は 2重送信を防ぐために読み飛ばす
                        if (i + 1 < bytesRead && buffer[i + 1] == 0x0A)
                        {
                            i++;
                        }
                        sendBuffer.Add(0x0D);
                        continue;
                    }

                    if (b == 0x0A) // LF 単独が来た場合も CR (0x0D) として送信
                    {
                        sendBuffer.Add(0x0D);
                        continue;
                    }

                    // IAC (255) は 255 255 にエスケープ
                    if (b == TelnetConstants.IAC)
                    {
                        sendBuffer.Add(TelnetConstants.IAC);
                        sendBuffer.Add(TelnetConstants.IAC);
                    }
                    else
                    {
                        sendBuffer.Add(b);
                    }
                }

                if (sendBuffer.Count > 0)
                {
                    await SendBytesAsync(sendBuffer.ToArray());
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (!_cts.IsCancellationRequested)
        {
            Console.WriteLine($"\r\n[Send error: {ex.Message}]");
        }
    }

    /// <summary>
    /// エスケーププロンプト (Win_tel>)
    /// </summary>
    private async Task<bool> HandleEscapePromptAsync()
    {
        _console.Restore();
        Console.WriteLine();
        Console.Write("Win_tel> ");

        var line = Console.ReadLine()?.Trim().ToLowerInvariant();
        _console.EnableRawMode();

        if (line is "q" or "quit" or "close" or "exit")
        {
            Console.WriteLine("Connection closed.");
            return false;
        }

        if (line == "status")
        {
            Console.WriteLine($"Connected to: {_host}:{_port}");
            Console.WriteLine($"Terminal-Type: {_terminalType}");
            Console.WriteLine($"NAWS negotiated: {_nawsEnabled} ({_console.CurrentWidth}x{_console.CurrentHeight})");
            Console.WriteLine($"Remote Echo: {_remoteEcho}");
        }

        return true;
    }

    private async Task HandleWillAsync(byte option)
    {
        switch (option)
        {
            case TelnetConstants.OPT_ECHO:
                _remoteEcho = true;
                await SendDoAsync(option);
                break;

            case TelnetConstants.OPT_SUPPRESS_GO_AHEAD:
                await SendDoAsync(option);
                await SendWillAsync(option); // 双方向 SGA
                break;

            case TelnetConstants.OPT_TRANSMIT_BINARY:
                await SendDoAsync(option);
                await SendWillAsync(option); // 双方向 8-bit バイナリ透過
                break;

            default:
                await SendDontAsync(option);
                break;
        }
    }

    private async Task HandleWontAsync(byte option)
    {
        if (option == TelnetConstants.OPT_ECHO)
        {
            _remoteEcho = false;
        }
        _myDos.Remove(option);
        await SendDontAsync(option);
    }

    private async Task HandleDoAsync(byte option)
    {
        switch (option)
        {
            case TelnetConstants.OPT_TERMINAL_TYPE:
                await SendWillAsync(option);
                break;

            case TelnetConstants.OPT_NAWS:
                _nawsEnabled = true;
                await SendWillAsync(option);
                // サーバーが DO NAWS を求めてきたら直ちに現在サイズを送信
                await SendWindowSizeAsync(_console.CurrentWidth, _console.CurrentHeight);
                break;

            case TelnetConstants.OPT_SUPPRESS_GO_AHEAD:
            case TelnetConstants.OPT_TRANSMIT_BINARY:
                await SendWillAsync(option);
                break;

            case TelnetConstants.OPT_ECHO:
                // クライアント側エコーは常に拒絶 (サーバーにエコーしてもらう)
                await SendWontAsync(option);
                break;

            default:
                await SendWontAsync(option);
                break;
        }
    }

    private async Task HandleDontAsync(byte option)
    {
        if (option == TelnetConstants.OPT_NAWS)
        {
            _nawsEnabled = false;
        }
        _myWills.Remove(option);
        await SendWontAsync(option);
    }

    private async Task HandleSubnegotiationAsync(byte option, byte[] data)
    {
        if (option == TelnetConstants.OPT_TERMINAL_TYPE)
        {
            if (data.Length > 0 && data[0] == TelnetConstants.SUB_SEND)
            {
                // IAC SB TERMINAL_TYPE IS <termType> IAC SE
                var termBytes = Encoding.ASCII.GetBytes(_terminalType);
                var packet = new List<byte>(termBytes.Length + 6)
                {
                    TelnetConstants.IAC,
                    TelnetConstants.SB,
                    TelnetConstants.OPT_TERMINAL_TYPE,
                    TelnetConstants.SUB_IS
                };
                packet.AddRange(termBytes);
                packet.Add(TelnetConstants.IAC);
                packet.Add(TelnetConstants.SE);

                await SendBytesAsync(packet.ToArray());
            }
        }
    }

    private void OnWindowResized(int width, int height)
    {
        if (_nawsEnabled)
        {
            _ = SendWindowSizeAsync(width, height);
        }
    }

    /// <summary>
    /// NAWS (RFC 1073) サブネゴシエーションでウィンドウサイズを送信
    /// </summary>
    public async Task SendWindowSizeAsync(int width, int height)
    {
        width = Math.Clamp(width, 1, 65535);
        height = Math.Clamp(height, 1, 65535);

        var packet = new List<byte>(12)
        {
            TelnetConstants.IAC,
            TelnetConstants.SB,
            TelnetConstants.OPT_NAWS
        };

        // 16bit big-endian
        AppendEscapedNawsByte(packet, (byte)(width >> 8));
        AppendEscapedNawsByte(packet, (byte)(width & 0xFF));
        AppendEscapedNawsByte(packet, (byte)(height >> 8));
        AppendEscapedNawsByte(packet, (byte)(height & 0xFF));

        packet.Add(TelnetConstants.IAC);
        packet.Add(TelnetConstants.SE);

        await SendBytesAsync(packet.ToArray());
    }

    private static void AppendEscapedNawsByte(List<byte> list, byte b)
    {
        list.Add(b);
        // IAC (255) はエスケープが必要
        if (b == TelnetConstants.IAC)
        {
            list.Add(TelnetConstants.IAC);
        }
    }

    private async Task SendWillAsync(byte option)
    {
        if (_myWills.Add(option))
        {
            await SendOptionAsync(TelnetConstants.WILL, option);
        }
    }

    private async Task SendWontAsync(byte option)
    {
        _myWills.Remove(option);
        await SendOptionAsync(TelnetConstants.WONT, option);
    }

    private async Task SendDoAsync(byte option)
    {
        if (_myDos.Add(option))
        {
            await SendOptionAsync(TelnetConstants.DO, option);
        }
    }

    private async Task SendDontAsync(byte option)
    {
        _myDos.Remove(option);
        await SendOptionAsync(TelnetConstants.DONT, option);
    }

    private async Task SendOptionAsync(byte verb, byte option)
    {
        byte[] packet = [TelnetConstants.IAC, verb, option];
        await SendBytesAsync(packet);
    }

    private async Task SendBytesAsync(byte[] bytes)
    {
        if (_stream == null) return;

        await _sendLock.WaitAsync(_cts.Token);
        try
        {
            await _stream.WriteAsync(bytes, 0, bytes.Length, _cts.Token);
            await _stream.FlushAsync(_cts.Token);
        }
        catch { }
        finally
        {
            _sendLock.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _console.WindowResized -= OnWindowResized;
        _cts.Cancel();
        _stream?.Dispose();
        _tcpClient?.Dispose();
        _sendLock.Dispose();
        _cts.Dispose();
    }
}
