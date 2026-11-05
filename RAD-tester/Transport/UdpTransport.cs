// UdpTransport.cs - RADIUS / UDP トランスポート

using System;
using System.Net;
using System.Net.Sockets;

namespace RadiusTest
{
    public class UdpTransport : ITransport
    {
        private Socket? _sock;
        private readonly Options _o;

        public UdpTransport(Options o) { _o = o; }

        public bool Open(IPEndPoint ep)
        {
            try
            {
                _sock = new Socket(ep.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
                if (ep.AddressFamily == AddressFamily.InterNetwork)
                {
                    try
                    {
                        // Windows SIO_UDP_CONNRESET: ICMP ポート到達不能時の自動リセット挙動を制御
                        const int SIO_UDP_CONNRESET = -1744830452;
                        _sock.IOControl(SIO_UDP_CONNRESET, new byte[] { 0 }, null);
                    }
                    catch { }
                }
                _sock.Connect(ep);
                Progress.Log("CONNECT", PState.Ok, $"UDP ソケット準備完了: ローカル {_sock.LocalEndPoint} -> サーバー {ep}", "Supplicant --> RADIUS");
                return true;
            }
            catch (Exception ex)
            {
                Progress.Log("CONNECT", PState.Fail, $"UDP ソケットの初期化に失敗しました: {ex.Message}", "Supplicant --> RADIUS");
                return false;
            }
        }

        public IPEndPoint? LocalEndPoint => _sock?.LocalEndPoint as IPEndPoint;

        public void Send(byte[] data)
        {
            _sock?.Send(data);
        }

        public byte[]? Receive(int timeoutMs)
        {
            if (_sock == null) return null;
            _sock.ReceiveTimeout = timeoutMs;
            byte[] buf = new byte[65536];
            try
            {
                int n = _sock.Receive(buf);
                byte[] res = new byte[n];
                Buffer.BlockCopy(buf, 0, res, 0, n);
                return res;
            }
            catch (SocketException se) when (se.SocketErrorCode == SocketError.ConnectionReset)
            {
                Progress.Log("RECV", PState.Fail, "ICMP port unreachable を受信しました（サーバー側 UDP ポートで待受していません）", "Supplicant <-- RADIUS");
                return null;
            }
            catch (SocketException se) when (se.SocketErrorCode == SocketError.TimedOut)
            {
                return null;
            }
            catch (SocketException se)
            {
                Progress.Log("RECV", PState.Fail, $"UDP 受信エラー ({se.SocketErrorCode}): {se.Message}", "Supplicant <-- RADIUS");
                return null;
            }
        }

        public void Close()
        {
            try { _sock?.Close(); } catch { }
            _sock = null;
        }

        public void Dispose() => Close();
    }
}
