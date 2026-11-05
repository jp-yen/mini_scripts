# mini_scripts


- alarm-window (PowerShell Win)<br />
  アラームウィンドウを表示。スクリプトと同じディレクトリにある a.wav を連続再生 (最後のアラームから 10分で停止)

- chrome-selection-copy (Chrome 拡張機能)<br />
  コピーが禁止されている文章を強引にコピー

- CPU Thermo Watcher (C# Win)<br />
  タスクトレイに常駐し、CPU や GPU の最高温度をリアルタイム表示。

- Editer-rename (C# & PowerShell Win)<br />
ファイル名の変更をエディタで行うツール。(C# 版と、PowerShell 版があり)
右クリックの送るから「Editor Rename Tool」を選択するとエディターとGUIが起動、エディターでファイル名を変更 & 保存し、GUIの「編集完了」をクリック(※)してリネームを実行します。

- fake_user.py (py)<br />
  AD のユーザーを PowerShell で登録できる形式 (CSV) でランダムに生成する

- firefox-selection-copy (Firefox 拡張機能)<br />
  コピーが禁止されている文章を強引にコピー

- font-selector (.Net10,WebView2 Win)<br />
  条件に一致するフォントを表示するツール

- GnuGP-UI (C# Win)<br />
  GnuGP の GUI フロントエンド<br />
  一時期 Kleopatra がうまく動作しなかったときに作成

- multiroute.cpp , multiroute_flow.md (c++)<br />
  各ホップへのICMPプローブを並列実行し、経路の変化を継続的にモニタリングするリアルタイム tracer route ツールです。

- MultiRoute.cs, MultiRoute.csproj (C# Win)<br />
  multiroute の .Net 9 版

- netrouteview_lng.ini<br />
  Windows のルーティングを GUI で確認、操作できるアプリケーションの日本語化ファイル<br />
  https://www.nirsoft.net/utils/network_route_view.html<br />
文字化けした場合、ファイルの文字コードを UTF-8 にしたり、SJIS にしたりしてください。<br />
システムの設定としてはスタートメニューで「地域」を検索、「管理」タブを開いて「Unicode 対応ではないプログラムの言語」の「システムロケールの変更」を開く<br />
「✅ベータ：ワールドワイド言語サポートで Unicode UTF-8 を使用」にチェックを入れたり外したり。

- py-ping (py)<br />
  ping の送信時刻を表示できる python スクリプト (root 権限が必要)

- RAD-tester (C# Win)<br />
  RADIUS および RadSec (TLS 1.3 / 相互証明書認証) の認証テストクライアント (CLI / GUI)。

- syslog-gen (py)<br />
  syslog メッセージを生成。
  src アドレスを偽装して送信する

- TeraTerm Macro.esy (EmEditor)<br />
  EmEditor の TeraTerm Macro 用スタイルファイル
  (スクリプトじゃないですが...)

- tty_clock.c (C FreeBSD, Debian)<br />
  ターミナル上で現在時刻を表示

- vyos-pppoe-generator.html (html VyOS)<br />
  VyOS を PPPoE サーバーとして使う時の設定生成ページ

- Windows-telnet (C# Win)<br />
  Windows Terminal で zsh や 256色・Unicode が崩れず動作するモダンな Telnet クライアント。<br />
