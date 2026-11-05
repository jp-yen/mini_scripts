// Program.cs - RADIUS / RadSec テストクライアント CLI エントリポイント & コマンドライン引数パーサー (.NET 10)

using System;
using System.Collections.Generic;
using System.Text;

namespace RadiusTest
{
    /// <summary>
    /// CLI のメインエントリポイントおよびコマンドライン引数の解析クラス
    /// </summary>
    public static class Program
    {
        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(int dwProcessId);

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeConsole();

        private const int ATTACH_PARENT_PROCESS = -1;

        [STAThread]
        public static int Main(string[] args)
        {
            if (args.Length == 0 || (args.Length == 1 && (args[0].Equals("-gui", StringComparison.OrdinalIgnoreCase) || args[0].Equals("--gui", StringComparison.OrdinalIgnoreCase))))
            {
                // GUI モード: コマンドプロンプト画面を一切開かずに Windows Forms を直接起動
                System.Windows.Forms.Application.EnableVisualStyles();
                System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
                System.Windows.Forms.Application.Run(new MainForm());
                return 0;
            }

            // CLI モード: リダイレクトされていない場合は呼び出し元の親コンソール (PowerShell / cmd) に接続
            bool isRedirected = Console.IsInputRedirected || Console.IsOutputRedirected || Console.IsErrorRedirected;
            if (!Console.IsOutputRedirected && !Console.IsErrorRedirected)
            {
                AttachConsole(ATTACH_PARENT_PROCESS);
            }
            try
            {
                var stdout = Console.OpenStandardOutput();
                Console.SetOut(new System.IO.StreamWriter(stdout, new UTF8Encoding(false)) { AutoFlush = true });
            }
            catch { }
            try
            {
                var stderr = Console.OpenStandardError();
                Console.SetError(new System.IO.StreamWriter(stderr, new UTF8Encoding(false)) { AutoFlush = true });
            }
            catch { }
            try { Console.OutputEncoding = Encoding.UTF8; } catch { }

            return RunCli(args, isRedirected);
        }

        private static int RunCli(string[] args, bool isRedirected = false)
        {
            var o = new Options();
            var pos = new List<string>();

            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (!a.StartsWith("-") || a == "-" || a == "--")
                {
                    pos.Add(a);
                    continue;
                }

                string key = a.TrimStart('-').ToLowerInvariant();
                switch (key)
                {
                    case "help":
                    case "h":
                    case "?":
                        PrintUsage();
                        return 0;

                    case "gencert":
                        string certOut = (++i < args.Length && !args[i].StartsWith("-")) ? args[i] : "test_cert.pem";
                        string keyOut = (++i < args.Length && !args[i].StartsWith("-")) ? args[i] : "test_key.pem";
                        try
                        {
                            CertHelper.GenerateSelfSignedCertificate(certOut, keyOut);
                            Console.WriteLine($"[+] 自己署名証明書を生成しました: {certOut}, 秘密鍵: {keyOut}");
                            return 0;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[-] 証明書生成エラー: {ex.Message}");
                            return 1;
                        }

                    case "radsec":
                        o.RadSec = true;
                        break;
                    case "6":
                    case "ipv6":
                        o.Ipv6 = true;
                        o.Ipv4 = false;
                        break;
                    case "4":
                    case "ipv4":
                        o.Ipv4 = true;
                        o.Ipv6 = false;
                        break;
                    case "status":
                        o.StatusServer = true;
                        break;
                    case "brief":
                        o.Brief = true;
                        break;
                    case "nohex":
                        o.NoHex = true;
                        break;
                    case "insecure":
                        o.Insecure = true;
                        break;
                    case "nonamecheck":
                        o.NoNameCheck = true;
                        break;
                    case "auth":
                        if (++i >= args.Length) return ArgError($"{a} の値が指定されていません。");
                        o.AuthMethod = args[i];
                        break;
                    case "chap":
                        o.AuthMethod = "chap";
                        break;
                    case "mschap":
                    case "mschapv2":
                    case "mschap2":
                        o.AuthMethod = "mschapv2";
                        break;

                    case "eap":
                        if (++i >= args.Length) return ArgError($"{a} の値が指定されていません。");
                        o.Eap = args[i];
                        break;
                    case "eap-tls":
                    case "eaptls":
                        o.Eap = "tls";
                        break;
                    case "eap-ttls":
                    case "eapttls":
                        o.Eap = "ttls";
                        break;
                    case "peap":
                    case "peapv0":
                    case "eap-peap":
                    case "eappeap":
                    case "eap-peapv0":
                    case "eappeapv0":
                        o.Eap = "peap";
                        break;
                    case "peapv1":
                    case "eap-peapv1":
                    case "eappeapv1":
                        o.Eap = "peapv1";
                        break;
                    case "gtc":
                    case "eap-gtc":
                    case "eapgtc":
                        o.Eap = "gtc";
                        break;
                    case "fast":
                    case "eap-fast":
                    case "eapfast":
                        o.Eap = "fast";
                        break;
                    case "teap":
                    case "eap-teap":
                    case "eapteap":
                        o.Eap = "teap";
                        break;
                    case "anonymous":
                    case "outer":
                    case "outerid":
                        if (++i >= args.Length) return ArgError($"{a} の値が指定されていません。");
                        o.OuterId = args[i];
                        break;
                    case "ttls-inner":
                    case "ttlsinner":
                    case "inner":
                        if (++i >= args.Length) return ArgError($"{a} の値が指定されていません。");
                        o.TtlsInner = args[i];
                        break;
                    case "eapmtu":
                        if (++i >= args.Length) return ArgError($"{a} の値が指定されていません。");
                        if (!int.TryParse(args[i], out o.EapMtu)) return ArgError($"無効な eapmtu の値です: {args[i]}");
                        break;

                    case "msgauth":
                        o.MsgAuth = true;
                        break;
                    case "nomsgauth":
                        o.MsgAuth = false;
                        break;

                    case "p":
                    case "port":
                        if (++i >= args.Length) return ArgError($"{a} の値が指定されていません。");
                        if (!int.TryParse(args[i], out o.Port)) return ArgError($"無効なポート番号です: {args[i]}");
                        break;

                    case "t":
                    case "timeout":
                        if (++i >= args.Length) return ArgError($"{a} の値が指定されていません。");
                        if (!int.TryParse(args[i], out o.Timeout)) return ArgError($"無効なタイムアウト秒数です: {args[i]}");
                        break;

                    case "r":
                    case "retries":
                        if (++i >= args.Length) return ArgError($"{a} の値が指定されていません。");
                        if (!int.TryParse(args[i], out o.Retries)) return ArgError($"無効な再送回数です: {args[i]}");
                        break;

                    case "secret":
                        if (++i >= args.Length) return ArgError($"{a} の値が指定されていません。");
                        o.Secret = args[i];
                        break;

                    case "server":
                        if (++i >= args.Length) return ArgError($"{a} の値が指定されていません。");
                        o.Server = args[i];
                        break;

                    case "user":
                        if (++i >= args.Length) return ArgError($"{a} の値が指定されていません。");
                        o.User = args[i];
                        break;

                    case "pass":
                        if (++i >= args.Length) return ArgError($"{a} の値が指定されていません。");
                        o.Pass = args[i];
                        break;

                    case "tls":
                        if (++i >= args.Length) return ArgError($"{a} の値が指定されていません。");
                        o.Tls = args[i];
                        break;

                    case "cert":
                        if (++i >= args.Length) return ArgError($"{a} の値が指定されていません。");
                        o.CertFile = args[i];
                        break;

                    case "key":
                        if (++i >= args.Length) return ArgError($"{a} の値が指定されていません。");
                        o.KeyFile = args[i];
                        break;

                    case "certpass":
                        if (++i >= args.Length) return ArgError($"{a} の値が指定されていません。");
                        o.CertPass = args[i];
                        break;

                    case "thumb":
                        if (++i >= args.Length) return ArgError($"{a} の値が指定されていません。");
                        o.Thumb = args[i];
                        break;

                    case "certsubject":
                        if (++i >= args.Length) return ArgError($"{a} の値が指定されていません。");
                        o.CertSubject = args[i];
                        break;

                    case "store":
                        if (++i >= args.Length) return ArgError($"{a} の値が指定されていません。");
                        o.StoreName = args[i];
                        break;

                    case "storeloc":
                        if (++i >= args.Length) return ArgError($"{a} の値が指定されていません。");
                        o.StoreLoc = args[i];
                        break;

                    case "ca":
                    case "cacert":
                        if (++i >= args.Length) return ArgError($"{a} の値が指定されていません。");
                        o.CaFiles.Add(args[i]);
                        break;

                    case "sni":
                        if (++i >= args.Length) return ArgError($"{a} の値が指定されていません。");
                        o.Sni = args[i];
                        break;

                    case "nasip":
                        if (++i >= args.Length) return ArgError($"{a} の値が指定されていません。");
                        o.NasIp = args[i];
                        break;

                    case "nasipv6":
                        if (++i >= args.Length) return ArgError($"{a} の値が指定されていません。");
                        o.NasIpv6 = args[i];
                        break;

                    case "nasid":
                    case "nasidentifier":
                        if (++i >= args.Length) return ArgError($"{a} の値が指定されていません。");
                        o.NasId = args[i];
                        break;

                    case "nasport":
                        if (++i >= args.Length) return ArgError($"{a} の値が指定されていません。");
                        if (!int.TryParse(args[i], out int npVal)) return ArgError($"無効な nasport の値です: {args[i]}");
                        o.NasPort = npVal;
                        break;

                    case "nasporttype":
                    case "nasport-type":
                        if (++i >= args.Length) return ArgError($"{a} の値が指定されていません。");
                        if (!int.TryParse(args[i], out int npt)) return ArgError($"無効な nasporttype の値です: {args[i]}");
                        o.NasPortType = npt;
                        break;

                    case "nasportid":
                    case "nasport-id":
                        if (++i >= args.Length) return ArgError($"{a} の値が指定されていません。");
                        o.NasPortId = args[i];
                        break;

                    case "svc":
                        if (++i >= args.Length) return ArgError($"{a} の値が指定されていません。");
                        if (!int.TryParse(args[i], out o.ServiceType)) return ArgError($"無効な service-type の値です: {args[i]}");
                        break;

                    case "calling":
                        if (++i >= args.Length) return ArgError($"{a} の値が指定されていません。");
                        o.Calling = args[i];
                        break;

                    case "called":
                    case "calledstationid":
                    case "called-station-id":
                        if (++i >= args.Length) return ArgError($"{a} の値が指定されていません。");
                        o.Called = args[i];
                        break;

                    case "attr":
                        if (++i >= args.Length) return ArgError($"{a} の値が指定されていません。");
                        o.Attrs.Add(args[i]);
                        break;

                    case "attrhex":
                        if (++i >= args.Length) return ArgError($"{a} の値が指定されていません。");
                        o.AttrsHex.Add(args[i]);
                        break;

                    default:
                        return ArgError($"不明なオプションです: {a}");
                }
            }

            // 位置引数のマッピング
            int pidx = 0;
            if (o.Server == null && pidx < pos.Count) o.Server = pos[pidx++];

            if (!o.RadSec)
            {
                if (o.Secret == null && pidx < pos.Count) o.Secret = pos[pidx++];
            }
            if (o.User == null && pidx < pos.Count) o.User = pos[pidx++];
            if (o.Pass == null && pidx < pos.Count) o.Pass = pos[pidx++];

            try
            {
                o.Finish();
            }
            catch (Exception ex)
            {
                return ArgError(ex.Message);
            }

            if (!isRedirected && !Console.IsInputRedirected)
            {
                Out.Prompt = (prompt) => Console.Write(prompt);
            }
            return Runner.Run(o);
        }

        private static int ArgError(string msg)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine($"エラー: {msg}");
            Console.ResetColor();
            Console.Error.WriteLine("-help でコマンドの使用方法を確認してください。");
            return 2;
        }

        private static void PrintUsage()
        {
            Console.WriteLine(@"RadiusTest (.NET 10) - RADIUS & RadSec 認証テストクライアント

使用法:
  RADIUS (UDP):
    RadiusTest.exe <server> <secret> <user> <password> [オプション]
    RadiusTest.exe <server> <secret> -status [オプション]

  RadSec (TLS 1.3 / 1.2):
    RadiusTest.exe -radsec <server> [<user> <password>] [オプション]

共通オプション:
  -gui                       GUI フロントエンドを起動 (引数なし起動時も自動的に GUI 起動)
  -6, -ipv6                  IPv6 アドレス (AAAA) を優先 / 強制
  -4, -ipv4                  IPv4 アドレス (A) を優先 / 強制
  -auth <method>             認証方式: pap, chap, mschapv2 (既定: pap)
  -chap                      -auth chap のショートカット (RFC 1994 / RFC 2865)
  -mschapv2                  -auth mschapv2 のショートカット (RFC 2759 / RFC 2548)
  -p, -port <num>            サーバーポート (既定: UDP 1812, RadSec 2083)
  -t, -timeout <sec>         応答タイムアウト秒数 (既定: 3)
  -r, -retries <num>         UDP 再送回数 (既定: 2)
  -secret <str>              共有シークレット (RadSec の既定値: 'radsec')
  -server <host>             対象サーバーのホスト名または IP アドレス (IPv6 リテラル対応)
  -user <username>           ユーザー名 (Access-Request)
  -pass <password>           パスワード
  -status                    Status-Server 生存確認パケットを送信 (RFC 5997)
  -brief                     パケット詳細表示を抑制 (サマリのみ表示)
  -nohex                     パケット詳細内の 16 進ダンプ出力を抑制
  -nasip <ip>                NAS-IP-Address (Type 4, RFC 2865) を設定 ('none' で抑止)
  -nasipv6 <ipv6>            NAS-IPv6-Address (Type 95, RFC 3162) を設定 ('none' で抑止)
  -nasid <str>               NAS-Identifier 属性を設定 (ホスト名など)
  -nasport <num>             NAS-Port 属性を設定 (論理インターフェース番号, 既定: 0)
  -nasporttype <num>         NAS-Port-Type 属性を設定 (接続方法: 19=Wireless-802.11, 15=Ethernet 等)
  -nasportid <str>           NAS-Port-Id 属性を設定 (ポート名、SSID)
  -called <str>              Called-Station-Id 属性を設定 (APのMAC:SSID など)
  -calling <str>             Calling-Station-Id 属性を設定
  -svc <num>                 Service-Type 属性を設定 (既定: 1=Login-User, -1 で省略)
  -attr <type|name>=<val>    任意の RADIUS 属性を追加 (複数指定可)
  -attrhex <type|name>=<hex> 任意の RADIUS 属性を 16 進形式で追加

RadSec & TLS オプション:
  -radsec                    RadSec (RADIUS over TLS) モードを有効化
  -tls <ver>                 TLS バージョン: 1.3, 1.2, 1.1, 1.0, auto (既定: auto)
  -cert <file>               クライアント証明書ファイル (.pfx または PEM .crt/.pem)
  -key <file>                クライアント秘密鍵ファイル (PEM .key ファイル)
  -certpass <pw>             PFX ファイルのパスワードまたは暗号化秘密鍵のパスフレーズ
  -thumb <hex>               証明書ストアから SHA-1 拇印でクライアント証明書を検索
  -certsubject <str>         証明書ストアから Subject 名の部分一致で証明書を検索
  -store <name>              証明書ストア名 (既定: 'My')
  -storeloc <loc>            ストア場所: 'CurrentUser' または 'LocalMachine'
  -cacert <file>             サーバー検証用の信頼する CA 証明書 (複数指定可)
  -insecure                  サーバー証明書の検証をスキップ (自己署名証明書を受入)
  -nonamecheck               サーバー証明書とホスト名の不一致を無視
  -sni <hostname>            TLS SNI (Server Name Indication) ホスト名の上書き

EAP 認証オプション:
  -eap <tls|ttls|peap|peapv1|gtc|fast|teap>  EAP 認証モードを有効化
  -eap-tls                   -eap tls のショートカット (RFC 5216 / 証明書認証)
  -eap-ttls                  -eap ttls のショートカット (RFC 5281 / 内部 PAP)
  -peap, -peapv0             PEAPv0 認証 (MS-PEAP / 内部 MS-CHAPv2)
  -peapv1, -eap-peapv1       PEAPv1 認証 (Cisco PEAP / 内部 EAP-GTC)
  -gtc, -eap-gtc             -eap gtc のショートカット (RFC 3748 / トークン認証)
  -fast, -eap-fast           -eap fast のショートカット (RFC 4851 / 内部 MS-CHAPv2)
  -teap, -eap-teap           -eap teap のショートカット (RFC 7170 / 次世代トンネル)
  -outer <identity>          EAP 外側 (匿名) ユーザー名 (既定: -user と同一)
  -ttls-inner <method>       EAP-TTLS 内部認証方式: pap (既定), mschapv2, chap
  -eapmtu <num>              EAP フラグメントごとの最大 TLS データサイズ (既定: 1000)

実行例:
  RadiusTest.exe 172.21.89.12 testing123 bob hello -p 1812
  RadiusTest.exe [fe80::215:5dff:feeb:8bde%78] testing123 bob hello -p 1812
  RadiusTest.exe radius.example.com testing123 bob hello -6
  RadiusTest.exe 172.21.89.12 testing123 bob hello -p 1812 -chap
  RadiusTest.exe 172.21.89.12 testing123 bob hello -p 1812 -mschapv2
  RadiusTest.exe 172.21.89.12 testing123 -status -p 1812
  RadiusTest.exe 172.21.89.12 testing123 bob hello -gtc
  RadiusTest.exe 172.21.89.12 testing123 bob -eap-tls -cert client.crt -key client.key -insecure
  RadiusTest.exe 172.21.89.12 testing123 bob hello -eap-ttls -insecure
  RadiusTest.exe 172.21.89.12 testing123 bob hello -peap -insecure
  RadiusTest.exe 172.21.89.12 testing123 bob hello -fast -insecure
  RadiusTest.exe 172.21.89.12 testing123 bob hello -teap -insecure
  RadiusTest.exe -radsec 172.21.89.12 bob hello -insecure
  RadiusTest.exe -radsec [fe80::215:5dff:feeb:8bde%78] bob hello -insecure
  RadiusTest.exe -radsec radius.example.com -tls 1.3 -cert client.crt -key client.key -cacert ca.crt
");
        }
    }
}
