using UnityEngine;
using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Unity.Plastic.Newtonsoft.Json;

namespace LightBakingResoLink {
    public class ResoLinkWebSocket {
        private ClientWebSocket socket;
        private CancellationTokenSource cancelSource;

        public bool IsConnected() {
            return socket != null && socket.State == WebSocketState.Open;
        }

        public async Task Connect(string url) {
            if (IsConnected()) return;

            socket = new ClientWebSocket();
            cancelSource = new CancellationTokenSource();

            try {
                await socket.ConnectAsync(new Uri(url), cancelSource.Token);
            } catch (Exception e) {
                Debug.LogError("WebSocket connection failed: " + e.Message);
            }
        }

        public async Task Disconnect() {
            if (socket == null) return;

            try {
                cancelSource?.Cancel();

                if (socket.State == WebSocketState.Open) {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Shutdown", CancellationToken.None);
                }
            } catch (Exception e) {
                Debug.LogError("WebSocket disconnection failed: " + e.Message);
            } finally {
                socket?.Dispose();
                cancelSource?.Dispose();
                socket = null;
                cancelSource = null;
            }
        }

        public async Task Send<T>(T data) {
            if (!IsConnected()) return;

            try {
                string json = JsonConvert.SerializeObject(data);
                byte[] bytes = Encoding.UTF8.GetBytes(json);
                await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancelSource.Token);
            } catch (Exception e) {
                Debug.LogError("WebSocket send failed: " + e.Message);
            }
        }

        public async Task<object> Receive() {
            if (!IsConnected()) return null;

            byte[] buffer = new byte[1024];
            StringBuilder completeMessage = new StringBuilder();
            WebSocketReceiveResult result = null;

            try {
                while (result == null || !result.EndOfMessage) {
                    result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancelSource.Token);

                    if (result.MessageType == WebSocketMessageType.Close) {
                        await Disconnect();
                        return null;
                    }

                    string chunk = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    completeMessage.Append(chunk);
                }

                string message = completeMessage.ToString();

                try {
                    return JsonConvert.DeserializeObject<object>(message);
                } catch {
                    Debug.LogWarning("Received message is not valid JSON");
                    return null;
                }
            } catch (OperationCanceledException) {
                return null;
            } catch (Exception e) {
                Debug.LogError("WebSocket receive failed: " + e.Message);
                return null;
            }
        }

        public void Dispose() {
            cancelSource?.Cancel();
            socket?.Dispose();
            cancelSource?.Dispose();
            socket = null;
            cancelSource = null;
        }
    }
}
