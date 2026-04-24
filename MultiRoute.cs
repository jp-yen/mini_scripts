// dotnet publish -c Release -r win-x64 --self-contained -p:PublishTrimmed=true -p:PublishSingleFile=true (最適化・単一ファイル化)
// dotnet build (とりあえずビルド)

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;

namespace MultiRoute
{
    // アプリケーションの設定を保持するレコード
    public record MultiRouteOptions(string Host, int MinTtl = 1, int MaxTtl = 7, int TimeoutMs = 500);

    class Program
    {
        static async Task<int> Main(string[] args)
        {
            // 1. 引数の解析
            if (!TryParseArgs(args, out var options))
            {
                PrintUsage();
                return 1;
            }

            // 2. ターゲットのIPアドレス解決
            var targetIP = await ResolveTargetIpAsync(options.Host);
            if (targetIP == null) return 1;

            // 3. キャンセルトークンの設定 (Ctrl+C対応)
            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            Console.WriteLine("Press Ctrl+C to stop\n");

            // 4. アプリケーションの組み立てと実行 (Dependency Injection的な構成)
            var engine = new MultiRouteEngine(new PingService(), new ConsoleReporter());
            await engine.RunAsync(targetIP, options, cts.Token);
            
            return 0;
        }
        
        static bool TryParseArgs(string[] args, out MultiRouteOptions options)
        {
            options = null!;
            if (args.Length == 0) return false;

            string host = args[0];
            int minTtl = 1;
            int maxTtl = 7;
            int timeoutMs = 500;

            for (int i = 1; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "-t" when i + 1 < args.Length:
                        var ttlArg = args[++i];
                        if (ttlArg.Contains(','))
                        {
                            var parts = ttlArg.Split(',');
                            if (parts.Length == 2 && int.TryParse(parts[0], out var pMin) && int.TryParse(parts[1], out var pMax))
                            {
                                minTtl = pMin;
                                maxTtl = pMax;
                            }
                        }
                        else
                        {
                            int.TryParse(ttlArg, out maxTtl);
                        }
                        break;
                    case "--timeout" when i + 1 < args.Length:
                        int.TryParse(args[++i], out timeoutMs);
                        break;
                }
            }
            
            if (minTtl < 1) minTtl = 1;
            if (maxTtl < minTtl) maxTtl = minTtl;

            options = new MultiRouteOptions(host, minTtl, maxTtl, timeoutMs);
            return true;
        }

        static void PrintUsage()
        {
            Console.WriteLine("Usage: multiroute <hostname/ip> [-t <max_ttl> | <min_ttl>,<max_ttl>] [--timeout <ms>]");
            Console.WriteLine("Example: multiroute 1.1.1.1 -t 7 --timeout 500");
            Console.WriteLine("         multiroute 1.1.1.1 -t 3,5");
        }

        static async Task<IPAddress?> ResolveTargetIpAsync(string host)
        {
            try
            {
                if (IPAddress.TryParse(host, out var ip))
                {
                    return ip;
                }

                var hostEntry = await Dns.GetHostEntryAsync(host);
                var resolvedIp = hostEntry.AddressList.FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
                
                if (resolvedIp == null)
                {
                    Console.WriteLine($"Could not resolve hostname: {host}");
                }
                return resolvedIp;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error resolving hostname: {ex.Message}");
                return null;
            }
        }
    }

    // Pingの実行を担当するインターフェース
    public interface IPingService
    {
        Task<string[]> ParallelPingAsync(IPAddress targetIP, int minTtl, int maxTtl, int timeoutMs, CancellationToken cancellationToken);
    }

    // 実際のPing処理を行うクラス
    public class PingService : IPingService
    {
        public async Task<string[]> ParallelPingAsync(IPAddress targetIP, int minTtl, int maxTtl, int timeoutMs, CancellationToken cancellationToken)
        {
            int count = maxTtl - minTtl + 1;
            var results = new string[count];
            using var semaphore = new SemaphoreSlim(Environment.ProcessorCount * 2);

            var tasks = Enumerable.Range(minTtl, count).Select(async ttl =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    results[ttl - minTtl] = await SendPingWithTtlAsync(targetIP, ttl, timeoutMs, cancellationToken);
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);
            return results;
        }

        private async Task<string> SendPingWithTtlAsync(IPAddress targetIP, int ttl, int timeoutMs, CancellationToken cancellationToken)
        {
            try
            {
                using var ping = new Ping();
                var options = new PingOptions(ttl, true);
                ReadOnlyMemory<byte> buffer = new byte[32];
                
                var reply = await ping.SendPingAsync(targetIP, timeoutMs, buffer.ToArray(), options);

                return reply.Status switch
                {
                    IPStatus.Success => reply.Address?.ToString() ?? "*",
                    IPStatus.TtlExpired or IPStatus.TimeExceeded => reply.Address?.ToString() ?? "*",
                    IPStatus.TimedOut => "!Time Out",
                    IPStatus.DestinationHostUnreachable => "!Host Unreach",
                    IPStatus.DestinationNetworkUnreachable => "!Net Unreach",
                    IPStatus.DestinationProtocolUnreachable => "!Protocol Unreach",
                    IPStatus.DestinationPortUnreachable => "!Port Unreach",
                    IPStatus.PacketTooBig => "!Frag Too Big",
                    _ => "!Unknown Error"
                };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return "*";
            }
        }
    }

    // 結果の出力を担当するインターフェース
    public interface IReporter
    {
        void PrintHeader(int minTtl, int maxTtl);
        void Report(string timestamp, string[] results);
    }

    // コンソールへの出力を担当するクラス
    public class ConsoleReporter : IReporter
    {
        public void PrintHeader(int minTtl, int maxTtl)
        {
            Console.Write("Time        |");
            for (int i = minTtl; i <= maxTtl; i++)
            {
                Console.Write($"Hop {i}".PadRight(15) + "|");
            }
            Console.WriteLine();
        }

        public void Report(string timestamp, string[] results)
        {
            Span<char> lineBuffer = stackalloc char[1024];
            var line = lineBuffer;
            var pos = 0;

            timestamp.AsSpan().CopyTo(line[pos..]);
            pos += timestamp.Length;
            line[pos++] = '|';

            for (int i = 0; i < results.Length; i++)
            {
                var result = results[i] ?? "*";
                var displayResult = result.Length > 15 ? result[..15] : result.PadRight(15);
                
                displayResult.AsSpan().CopyTo(line[pos..]);
                pos += displayResult.Length;
                line[pos++] = '|';
            }

            Console.WriteLine(line[..pos].ToString());
        }
    }

    // 全体のフローとループを管理するエンジンダークラス
    public class MultiRouteEngine
    {
        private readonly IPingService _pingService;
        private readonly IReporter _reporter;

        public MultiRouteEngine(IPingService pingService, IReporter reporter)
        {
            _pingService = pingService;
            _reporter = reporter;
        }

        public async Task RunAsync(IPAddress targetIP, MultiRouteOptions options, CancellationToken cancellationToken)
        {
            Console.WriteLine($"Starting multiroute to {options.Host} ({targetIP}) with TTL {options.MinTtl} to {options.MaxTtl}");
            _reporter.PrintHeader(options.MinTtl, options.MaxTtl);
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));

            try
            {
                do
                {
                    var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
                    var results = await _pingService.ParallelPingAsync(targetIP, options.MinTtl, options.MaxTtl, options.TimeoutMs, cancellationToken);
                    _reporter.Report(timestamp, results);
                    
                } while (await timer.WaitForNextTickAsync(cancellationToken));
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("\nMultiroute stopped.");
            }
        }
    }
}