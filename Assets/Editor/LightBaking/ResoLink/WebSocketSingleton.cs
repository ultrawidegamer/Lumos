using UnityEditor;
using UnityEngine;
using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Unity.Plastic.Newtonsoft.Json;

public class ResoLinkWebSocketSingleton : ScriptableSingleton<ResoLinkWebSocketSingleton> {
    private ClientWebSocket socket;
    private CancellationTokenSource cancelSource;

    public bool IsConnected() {
        return socket != null && socket.State == WebSocketState.Open;
    }

    public async Task Connect(string url) {
        if (IsConnected()) {
            Debug.Log("Already connected.");
            return;
        }

        socket = new ClientWebSocket();
        cancelSource = new CancellationTokenSource();

        try {
            await socket.ConnectAsync(new Uri(url), cancelSource.Token);
            Debug.Log("WebSocket connected.");
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

            Debug.Log("WebSocket disconnected.");
        } catch (Exception e) {
            Debug.LogError("WebSocket disconnection failed: " + e.Message);
        } finally {
            socket?.Dispose();
            socket = null;
        }
    }

    public async Task Send<T>(T data) {
        if (!IsConnected()) {
            Debug.LogWarning("Cannot send: WebSocket is not connected.");
            return;
        }

        try {
            string json = JsonConvert.SerializeObject(data);
            Debug.Log("Sending: " + json);
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancelSource.Token);
        } catch (Exception e) {
            Debug.LogError("WebSocket send failed: " + e.Message);
        }
    }

    public async Task<object> Receive() {
        if (!IsConnected()) {
            throw new InvalidOperationException("Cannot receive: WebSocket is not connected.");
        }

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

    private void OnDisable() {
        cancelSource?.Cancel();
        socket?.Dispose();
        socket = null;
    }
}
