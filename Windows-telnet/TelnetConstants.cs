namespace Windows_telnet;

/// <summary>
/// Telnet プロトコル (RFC 854 等) の定数定義
/// </summary>
public static class TelnetConstants
{
    // Telnet コマンド (RFC 854)
    public const byte SE   = 240; // Subnegotiation End
    public const byte NOP  = 241; // No Operation
    public const byte DM   = 242; // Data Mark
    public const byte BRK  = 243; // Break
    public const byte IP   = 244; // Interrupt Process
    public const byte AO   = 245; // Abort Output
    public const byte AYT  = 246; // Are You There
    public const byte EC   = 247; // Erase Character
    public const byte EL   = 248; // Erase Line
    public const byte GA   = 249; // Go Ahead
    public const byte SB   = 250; // Subnegotiation Begin
    public const byte WILL = 251; // Will perform option
    public const byte WONT = 252; // Refuse to perform option
    public const byte DO   = 253; // Request other party to perform option
    public const byte DONT = 254; // Demand other party not perform option
    public const byte IAC  = 255; // Interpret As Command

    // Telnet オプション
    public const byte OPT_TRANSMIT_BINARY   = 0;  // RFC 856: 8bit バイナリ透過
    public const byte OPT_ECHO              = 1;  // RFC 857: エコー
    public const byte OPT_SUPPRESS_GO_AHEAD = 3;  // RFC 858: Suppress Go Ahead
    public const byte OPT_STATUS            = 5;  // RFC 859: ステータス
    public const byte OPT_TIMING_MARK       = 6;  // RFC 860: タイミングマーク
    public const byte OPT_TERMINAL_TYPE     = 24; // RFC 1091: 端末タイプ
    public const byte OPT_NAWS              = 31; // RFC 1073: ウィンドウサイズ交渉
    public const byte OPT_TERMINAL_SPEED    = 32; // RFC 1079: 端末速度
    public const byte OPT_LINEMODE          = 34; // RFC 1184: 行モード
    public const byte OPT_NEW_ENVIRON       = 39; // RFC 1572: 環境変数
    public const byte OPT_CHARSET           = 42; // RFC 2066: キャラクタセット

    // サブネゴシエーション修飾子
    public const byte SUB_IS   = 0;
    public const byte SUB_SEND = 1;

    // デフォルトのエスケープ文字 (Ctrl+] = 0x1D)
    public const byte DEFAULT_ESCAPE_CHAR = 0x1D;
}
