# Windows-telnet

Windows Terminal 上で動作し、Debian 等の Linux 上で動く zsh の画面制御、Unicode 表示、カラー表示、ウィンドウリサイズに完全対応したモダンな Telnet クライアントです。

---

## 1. 背景と課題

### 課題
Windows 標準の `telnet.exe` を使用して Debian に接続すると、以下の問題が発生します。
- zsh のプロンプト（Starship / Powerlevel10k 等）や行編集機能（ZLE）、Tab 補完候補の描画が激しく崩れる。
- 日本語や Unicode 特殊文字（Powerline グリフ・絵文字等）が文字化けする。
- ターミナルのウィンドウサイズを変更してもサーバー側に伝わらず、行折り返しや右プロンプトの位置が狂う。
- 矢印キーやショートカットキー（Ctrl+C, Ctrl+Z 等）の挙動が正しく透過されない。

### 原因
1. **端末種別（TERMINAL-TYPE: RFC 1091）**: `ANSI` や `VT100` などの古い端末種別を通知するため、サーバー側が 256 色や高度な VT エスケープシーケンスを無効化してしまう。
2. **ウィンドウサイズ通知（NAWS: RFC 1073）の非対応**: クライアントからターミナルの行数・列数を通知せず、リサイズイベントも送出しないため、Debian 側で `SIGWINCH` シグナルが発生しない。
3. **8bit バイナリ透過（TRANSMIT-BINARY: RFC 856）の欠如**: 7bit 前提で通信され、UTF-8 のマルチバイト文字が欠落・破損する。
4. **コンソールの Raw モード制御の不備**: Windows 標準 telnet が古い Win32 Console API に依存しており、ConPTY / Windows Terminal のモダンなエスケープシーケンス処理を活かせていない。

---

## 2. 要件定義

### 2.1 機能要件

#### (1) Telnet プロトコル制御・RFC 準拠オプションネゴシエーション
- **TRANSMIT-BINARY (RFC 856)**:
  - 8bit バイナリ透過モードを交渉。UTF-8 マルチバイト文字を無加工で送受信。
- **ECHO (RFC 857) & SGA (RFC 858: Suppress Go Ahead)**:
  - サーバー主導のエコーバック、および Go Ahead 抑制により、完全なキャラクター単位（Raw）双方向通信を実現。
- **TERMINAL-TYPE (RFC 1091)**:
  - 端末タイプとして `xterm-256color`（または `xterm`）をサーバーに通知。
- **NAWS (RFC 1073: Negotiate About Window Size)**:
  - 接続確立時に、現在のターミナルサイズ（幅・高さ）をサブネゴシエーションで送信。
  - **動的リサイズ検知**: Windows Terminal のウィンドウサイズ変更をリアルタイムに検知し、即座に新しいサイズを NAWS で再送（Debian 上の zsh に `SIGWINCH` を発火させ、プロンプトを自動再描画）。

#### (2) ターミナル・コンソール制御
- **Windows Terminal / ConPTY 連携**:
  - `ENABLE_VIRTUAL_TERMINAL_PROCESSING`（VT 出力）および `ENABLE_VIRTUAL_TERMINAL_INPUT`（VT 入力）を有効化。
  - ローカルエコーおよび行入力モードを無効化（Raw モード）。
  - キー入力（矢印キー、Home/End、PageUp/Down、Tab、F1-F12 など）を VT シーケンスとしてそのまま透過送信。
- **文字コード**: 入出力ストリームを UTF-8 で統一。
- **シグナル透過**:
  - `Ctrl+C`、`Ctrl+Z`、`Ctrl+D` 等のキー入力をクライアント側で終了シグナルとして横取りせず、そのままリモートホストへ送信。

#### (3) 接続・セッション管理
- サーバー切断（ソケットクローズ）の正常検知とターミナル設定の復元・安全な終了。
- エスケープキー（`Ctrl+]`）によるセッション一時中断・コマンドモード（`Win_tel>` プロンプト）。
  - `q`, `quit`, `close`, `exit`: セッションを切断して終了
  - `status`: 接続先、端末タイプ、NAWS 状態などを表示
  - `[Enter]`: セッションにそのまま復帰

---

### 2.2 非機能要件

| 項目 | 仕様 |
| :--- | :--- |
| **開発環境・言語** | C# / .NET 10 SDK (`net10.0`) |
| **外部依存ライブラリ** | なし（.NET 標準ライブラリ + Win32 Console API P/Invoke のみで完結） |
| **動作環境** | Windows 10 (1809以降) / Windows 11 の Windows Terminal, PowerShell, cmd.exe |
| **配布形態** | 単一実行ファイル（Self-contained / Single-file exe 発行可能） |

---

## 3. CLI インターフェース仕様

```
使用法:
  telnet <ホスト名またはIP> [ポート番号] [オプション]

引数:
  <host>              接続先ホスト名または IP アドレス (必須)
  [port]              ポート番号 (デフォルト: 23)

オプション:
  -p, --port <port>   ポート番号 (デフォルト: 23)
  -t, --term <type>   通知する端末タイプ (デフォルト: xterm-256color)
  -e, --escape <char> エスケープキー (デフォルト: ^] )
  -?, -h, --help      ヘルプ情報を表示
```

---

## 4. プロジェクト構成

```
Windows-telnet/
├── README.md               # 本要件定義・仕様書
├── Windows-telnet.csproj   # .NET 10 プロジェクト定義 (AssemblyName: telnet)
├── Program.cs              # CLI エントリポイント・引数解析・終了処理
├── TelnetClient.cs         # TCP ソケット通信 & Telnet プロトコル/IAC ネゴシエーション
├── ConsoleController.cs    # Win32 Console API / ConPTY / Raw モード制御 & リサイズ監視
└── TelnetConstants.cs      # RFC オプション・コマンド定数定義
```

---

## 5. ビルド & 実行方法

### (1) 直接実行
```powershell
# ヘルプ表示
dotnet run -- --help

# Debian サーバーへ接続 (ポート23)
dotnet run -- debian-server.local

# ポート指定
dotnet run -- debian-server.local 23
```

### (2) 単一実行ファイル (`telnet.exe`) の発行

#### 方法A: バッチファイルを実行（推奨）
エクスプローラーから build.bat をダブルクリックするか、ターミナルで実行します。
```cmd
build.bat
```
成果物は `dist\telnet.exe` に自動生成されます。

#### 方法B: コマンドラインから直接発行
```powershell
dotnet publish -c Release -r win-x64 --no-self-contained -o dist
```

生成された `dist\telnet.exe` を PATH の通ったディレクトリに配置することで、Windows Terminal から直接 `telnet <host>` として呼び出すことが可能です。

