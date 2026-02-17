using UnityEngine;
using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Unity.Plastic.Newtonsoft.Json;
using System.Buffers;

namespace LightBakingResoLink {
    public class ResoLinkWebSocket {
        private ClientWebSocket socket;
        private CancellationTokenSource cancelSource;
        private const int BUFFER_SIZE = 64 * 1024;
        private SemaphoreSlim sendLock = null;
        private SemaphoreSlim receiveLock = null;
        private SemaphoreSlim messageAvailable = null;

        public bool IsConnected() {
            return socket != null && socket.State == WebSocketState.Open;
        }

        public async Task Connect(string url) {
            if (IsConnected()) return;

            sendLock = new SemaphoreSlim(1, 1);
            receiveLock = new SemaphoreSlim(1, 1);
            messageAvailable = new SemaphoreSlim(0);

            socket = new ClientWebSocket();
            socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);            
            cancelSource = new CancellationTokenSource();

            try {
                await socket.ConnectAsync(new Uri(url), cancelSource.Token);
            } catch (Exception e) {
                Debug.LogError("WebSocket connection failed: " + e.Message);
            }
        }

        public void Disconnect() {
            if (socket == null) return;

            try {
                cancelSource?.Cancel();
                socket.Abort();
            } catch (Exception e) {
                Debug.LogError("WebSocket force disconnection failed: " + e.Message);
            } finally {
                socket?.Dispose();
                cancelSource?.Dispose();
                sendLock?.Dispose();
                receiveLock?.Dispose();
                socket = null;
                cancelSource = null;
            }
        }

        public async Task Send<T>(T data) {
            if (!IsConnected()) {
                Debug.LogWarning("Cannot send: WebSocket not connected");
                return;
            }

            await sendLock.WaitAsync();
            try {
                if (!IsConnected()) return;
                string json = JsonConvert.SerializeObject(data);
                byte[] bytes = Encoding.UTF8.GetBytes(json);

                await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
            } catch (OperationCanceledException) {
                // Expected during shutdown - don't log
            } catch (Exception e) {
                Debug.LogError("WebSocket send failed: " + e.Message);
            } finally {
                sendLock.Release();
            }
        }

        public async Task<T> Receive<T>() {
            if (!IsConnected()) return default(T);

            byte[] rentedBuffer = ArrayPool<byte>.Shared.Rent(BUFFER_SIZE);
            await receiveLock.WaitAsync();
            try {
                int totalBytes = 0;
                WebSocketReceiveResult result = null;

                while (IsConnected() && (result == null || !result.EndOfMessage)) {
                    int remainingSpace = rentedBuffer.Length - totalBytes;
                    if (remainingSpace <= 0) {
                        byte[] newBuffer = ArrayPool<byte>.Shared.Rent(rentedBuffer.Length * 2);
                        Buffer.BlockCopy(rentedBuffer, 0, newBuffer, 0, totalBytes);
                        ArrayPool<byte>.Shared.Return(rentedBuffer);
                        rentedBuffer = newBuffer;
                        remainingSpace = rentedBuffer.Length - totalBytes;
                    }

                    result = await socket.ReceiveAsync(new ArraySegment<byte>(rentedBuffer, totalBytes, remainingSpace), CancellationToken.None);

                    if (!IsConnected()) return default(T);

                    if (result.MessageType == WebSocketMessageType.Close) {
                        Disconnect();
                        return default(T);
                    }

                    totalBytes += result.Count;
                }

                if (!IsConnected()) return default(T);

                string message = Encoding.UTF8.GetString(rentedBuffer, 0, totalBytes);

                return JsonConvert.DeserializeObject<T>(message);
            } catch (OperationCanceledException) {
                return default(T);
            } catch (Exception e) {
                Debug.LogError("WebSocket receive failed: " + e.Message);
                return default(T);
            } finally {
                ArrayPool<byte>.Shared.Return(rentedBuffer);
                receiveLock.Release();
            }
        }

        public void Dispose() {
            Debug.Log("Disposing ResoLinkWebSocket");
            cancelSource?.Cancel();
            socket?.Dispose();
            cancelSource?.Dispose();
            sendLock?.Dispose();
            receiveLock?.Dispose();
            messageAvailable?.Dispose();
            socket = null;
            cancelSource = null;
        }
    }
}
