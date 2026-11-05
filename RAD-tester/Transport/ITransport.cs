// ITransport.cs - トランスポート層抽象化インターフェース

using System;
using System.Net;

namespace RadiusTest
{
    // ------------------------------------------------------------------
    // トランスポート層抽象化 (UDP & RadSec TCP/TLS)
    // ------------------------------------------------------------------
    public interface ITransport : IDisposable
    {
        bool Open(IPEndPoint ep);
        void Send(byte[] data);
        byte[]? Receive(int timeoutMs);
        void Close();
        IPEndPoint? LocalEndPoint { get; }
    }
}
