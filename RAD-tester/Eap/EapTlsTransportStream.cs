// EapTlsTransportStream.cs - SslStream 直結用の双方向メモリパイプストリーム

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Security;
using System.Threading;

namespace RadiusTest
{
    // ------------------------------------------------------------------
    // SslStream 直結用の双方向メモリパイプストリーム
    // ------------------------------------------------------------------
    public class EapTlsTransportStream : Stream
    {
        private readonly Queue<byte[]> _inQueue = new Queue<byte[]>();
        private readonly AutoResetEvent _dataAvailable = new AutoResetEvent(false);
        private readonly MemoryStream _outBuffer = new MemoryStream();
        private readonly object _lock = new object();
        private bool _isDisposed;
        private byte[]? _currentInChunk;
        private int _currentInOffset;

        public override bool CanRead => true;
        public override bool CanWrite => true;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Write(byte[] buffer, int offset, int count)
        {
            ValidateBufferArguments(buffer, offset, count);
            if (count == 0) return;
            lock (_lock)
            {
                if (_isDisposed) throw new ObjectDisposedException(nameof(EapTlsTransportStream));
                _outBuffer.Write(buffer, offset, count);
            }
        }

        public byte[] ExtractOutgoingTls()
        {
            lock (_lock)
            {
                if (_outBuffer.Length == 0) return Array.Empty<byte>();
                byte[] bytes = _outBuffer.ToArray();
                _outBuffer.SetLength(0);
                return bytes;
            }
        }

        public void FeedIncomingTls(byte[] tlsBytes)
        {
            if (tlsBytes == null || tlsBytes.Length == 0) return;
            lock (_lock)
            {
                if (_isDisposed) return;
                _inQueue.Enqueue(tlsBytes);
                try { _dataAvailable.Set(); } catch { }
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            ValidateBufferArguments(buffer, offset, count);
            if (count == 0) return 0;

            while (true)
            {
                lock (_lock)
                {
                    if (_isDisposed) return 0;
                    if (_currentInChunk != null && _currentInOffset < _currentInChunk.Length)
                    {
                        int toCopy = Math.Min(count, _currentInChunk.Length - _currentInOffset);
                        Buffer.BlockCopy(_currentInChunk, _currentInOffset, buffer, offset, toCopy);
                        _currentInOffset += toCopy;
                        return toCopy;
                    }

                    if (_inQueue.Count > 0)
                    {
                        _currentInChunk = _inQueue.Dequeue();
                        _currentInOffset = 0;
                        continue;
                    }
                }

                bool signaled;
                try
                {
                    signaled = _dataAvailable.WaitOne(15000);
                }
                catch (ObjectDisposedException)
                {
                    return 0;
                }

                if (_isDisposed) return 0;

                if (!signaled)
                {
                    throw new TimeoutException("EAP ストリーム内での TLS レコード待機がタイムアウトしました。");
                }
            }
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _isDisposed = true;
                try { _dataAvailable.Set(); } catch { }
                _outBuffer.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
