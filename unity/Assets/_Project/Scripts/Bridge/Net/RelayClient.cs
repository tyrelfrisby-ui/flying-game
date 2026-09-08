using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FlyingGame.Bridge.Net
{
    /// <summary>
    /// Thin ClientWebSocket wrapper (System.Net.WebSockets works on iOS IL2CPP with .NET Standard 2.1).
    /// Reads on a background Task and parses JSON there; parsed messages land in <see cref="Inbox"/>,
    /// which NetSession drains on the main thread in Update. No Unity API is touched off-main.
    /// Sends are fire-and-forget; if the previous send hasn't finished (backpressure) the packet is dropped —
    /// the next 10 Hz state supersedes it anyway.
    /// </summary>
    public sealed class RelayClient : IDisposable
    {
        public readonly ConcurrentQueue<NetMsg> Inbox = new();

        private ClientWebSocket _ws;
        private CancellationTokenSource _cts;
        private int _sending;              // 0/1 gate for the single in-flight send
        private volatile bool _closed;
        private volatile string _error;

        public bool IsOpen => !_closed && _ws != null && _ws.State == WebSocketState.Open;
        public bool IsClosed => _closed;
        public string Error => _error;

        /// <summary>Connect and start the receive loop. Throws on failure (caller applies backoff).</summary>
        public async Task ConnectAsync(string url, int timeoutMs = 8000)
        {
            _ws = new ClientWebSocket();
            _cts = new CancellationTokenSource();
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            connectCts.CancelAfter(timeoutMs);
            try
            {
                await _ws.ConnectAsync(new Uri(url), connectCts.Token).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                _error = e.Message;
                _closed = true;
                throw;
            }
            ClientWebSocket ws = _ws; CancellationToken ct = _cts.Token;
            _ = Task.Run(() => ReceiveLoop(ws, ct));
        }

        public void Send(string json)
        {
            if (!IsOpen) return;
            if (Interlocked.CompareExchange(ref _sending, 1, 0) != 0) return;   // previous send still in flight: drop
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            _ = SendTask(_ws, _cts.Token, bytes);
        }

        private async Task SendTask(ClientWebSocket ws, CancellationToken ct, byte[] bytes)
        {
            try
            {
                await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                if (!_closed) { _error = e.Message; _closed = true; }
            }
            finally
            {
                Interlocked.Exchange(ref _sending, 0);
            }
        }

        private async Task ReceiveLoop(ClientWebSocket ws, CancellationToken ct)
        {
            var chunk = new byte[8192];
            var ms = new MemoryStream();
            try
            {
                while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
                {
                    ms.SetLength(0);
                    WebSocketReceiveResult r;
                    do
                    {
                        r = await ws.ReceiveAsync(new ArraySegment<byte>(chunk), ct).ConfigureAwait(false);
                        if (r.MessageType == WebSocketMessageType.Close)
                        {
                            _closed = true;
                            return;
                        }
                        ms.Write(chunk, 0, r.Count);
                    } while (!r.EndOfMessage);

                    if (r.MessageType != WebSocketMessageType.Text) continue;
                    string json = Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
                    NetMsg msg;
                    try { msg = NetMsg.FromJson(json); }
                    catch { continue; }
                    if (msg != null) Inbox.Enqueue(msg);
                }
            }
            catch (Exception e)
            {
                if (!ct.IsCancellationRequested) _error = e.Message;
            }
            finally
            {
                _closed = true;
            }
        }

        /// <summary>Close from any thread; safe to call repeatedly.</summary>
        public void Close()
        {
            if (_closed && _ws == null) return;
            _closed = true;
            var ws = _ws;
            var cts = _cts;
            _ws = null;
            _cts = null;
            if (ws == null) return;
            _ = Task.Run(async () =>
            {
                try
                {
                    if (ws.State == WebSocketState.Open)
                    {
                        using var t = new CancellationTokenSource(1500);
                        await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", t.Token).ConfigureAwait(false);
                    }
                }
                catch { /* best effort */ }
                try { cts?.Cancel(); } catch { }
                try { ws.Dispose(); } catch { }
                try { cts?.Dispose(); } catch { }
            });
        }

        public void Dispose() => Close();
    }
}
