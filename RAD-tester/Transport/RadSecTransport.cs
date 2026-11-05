// RadSecTransport.cs - RadSec (RADIUS over TCP/TLS) トランスポート

using System;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;

namespace RadiusTest
{
    public class RadSecTransport : ITransport
    {
        private TcpClient? _tcp;
        private SslStream? _ssl;
        private readonly Options _o;
        private readonly TlsContext _tls;

        public RadSecTransport(Options o, TlsContext tls) { _o = o; _tls = tls; }

        public bool Open(IPEndPoint ep)
        {
            try
            {
                _tcp = new TcpClient(ep.AddressFamily);
                var ar = _tcp.BeginConnect(ep.Address, ep.Port, null, null);
                if (!ar.AsyncWaitHandle.WaitOne(_o.Timeout * 1000))
                {
                    Progress.Log("CONNECT", PState.Fail, $"TCP 接続がタイムアウトしました ({_o.Timeout}秒)", "Supplicant --> RADIUS");
                    return false;
                }
                _tcp.EndConnect(ar);
                Progress.Log("CONNECT", PState.Ok, $"TCP 接続確立: ローカル {_tcp.Client.LocalEndPoint} -> サーバー {ep}", "Supplicant --> RADIUS");
            }
            catch (Exception ex)
            {
                Progress.Log("CONNECT", PState.Fail, $"TCP 接続に失敗しました: {ex.Message}", "Supplicant --> RADIUS");
                return false;
            }

            try
            {
                _ssl = new SslStream(_tcp.GetStream(), false, _tls.ValidateServerCertificate, _tls.SelectClientCertificate);
                string targetHost = _o.Sni ?? _o.Server ?? ep.Address.ToString();
                // IPv6 リンクローカルアドレスの場合、スコープ ID (例: %78) をホスト名から除去
                if (targetHost.Contains('%')) targetHost = targetHost.Split('%')[0];
                var certs = _tls.ClientCert != null ? new X509CertificateCollection { _tls.ClientCert } : null;

                _ssl.AuthenticateAsClient(targetHost, certs, _tls.GetProtocols(), false);

                string info = $"{_ssl.SslProtocol}, 暗号スイート {_ssl.NegotiatedCipherSuite}";
                Progress.Log("TLS", PState.Ok, $"TLS ハンドシェイク完了: {info}", "Supplicant <--> RADIUS");

                if (_ssl.RemoteCertificate != null)
                {
                    var sc = new X509Certificate2(_ssl.RemoteCertificate);
                    Progress.Info("TLS", $"サーバー証明書: {sc.Subject} (発行者: {sc.Issuer})");
                }
                if (_ssl.IsMutuallyAuthenticated)
                {
                    Progress.Info("TLS", "相互 TLS 確立（サーバーがクライアント証明書を受入）");
                }
                return true;
            }
            catch (Exception ex)
            {
                Progress.Log("TLS", PState.Fail, $"TLS ハンドシェイクに失敗しました: {ex.Message}", "Supplicant <--> RADIUS");
                return false;
            }
        }

        public IPEndPoint? LocalEndPoint => _tcp?.Client?.LocalEndPoint as IPEndPoint;

        public void Send(byte[] data)
        {
            _ssl?.Write(data, 0, data.Length);
            _ssl?.Flush();
        }

        public byte[]? Receive(int timeoutMs)
        {
            if (_ssl == null || _tcp == null) return null;
            _tcp.ReceiveTimeout = timeoutMs;

            byte[] header = new byte[4];
            int got = 0;
            try
            {
                while (got < 4)
                {
                    int r = _ssl.Read(header, got, 4 - got);
                    if (r <= 0) return null;
                    got += r;
                }
                int pktLen = (header[2] << 8) | header[3];
                if (pktLen < 20 || pktLen > 4096) throw new InvalidDataException($"無効な RADIUS パケット長です: {pktLen}");

                byte[] buf = new byte[pktLen];
                Buffer.BlockCopy(header, 0, buf, 0, 4);
                while (got < pktLen)
                {
                    int r = _ssl.Read(buf, got, pktLen - got);
                    if (r <= 0) return null;
                    got += r;
                }
                return buf;
            }
            catch (IOException ex) when (ex.InnerException is SocketException se && (se.SocketErrorCode == SocketError.TimedOut || se.SocketErrorCode == SocketError.ConnectionReset))
            {
                return null;
            }
            catch (SocketException se) when (se.SocketErrorCode == SocketError.TimedOut || se.SocketErrorCode == SocketError.ConnectionReset)
            {
                return null;
            }
            catch (Exception ex) when (ex is IOException or SocketException)
            {
                Progress.Log("RECV", PState.Fail, $"RadSec 接続エラー: {ex.Message}", "Supplicant <-- RADIUS");
                return null;
            }
        }

        public void Close()
        {
            try { _ssl?.Close(); } catch { }
            try { _tcp?.Close(); } catch { }
            _ssl = null; _tcp = null;
        }

        public void Dispose() => Close();
    }
}
