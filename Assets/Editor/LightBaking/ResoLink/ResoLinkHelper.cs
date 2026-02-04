using System;
using System.Threading.Tasks;
using UnityEngine;

namespace LightBakingResoLink {
    public class ResoLinkHelper {
        private static ResoLinkHelper instance;
        private ResoLinkWebSocket resoLinkWebSocket;
        private static readonly object lockObj = new object();

        public static ResoLinkHelper Instance {
            get {
                if (instance == null) {
                    lock (lockObj) {
                        if (instance == null) {
                            instance = new ResoLinkHelper();
                        }
                    }
                }
                return instance;
            }
        }

        private ResoLinkHelper() {
            resoLinkWebSocket = new ResoLinkWebSocket();
        }

        public async Task<bool> ConnectAsync(string url) {
            if (string.IsNullOrEmpty(url)) {
                return false;
            }

            try {
                await resoLinkWebSocket.Connect(url);
                return resoLinkWebSocket.IsConnected();
            } catch (Exception e) {
                Debug.LogError($"Failed to connect to ResoLink: {e.Message}");
                return false;
            }
        }

        public async Task DisconnectAsync() {
            try {
                await resoLinkWebSocket.Disconnect();
            } catch (Exception e) {
                Debug.LogError($"Failed to disconnect from ResoLink: {e.Message}");
            }
        }

        public bool IsConnected() {
            return resoLinkWebSocket != null && resoLinkWebSocket.IsConnected();
        }

        public async Task SendAsync<T>(T data) {
            if (!IsConnected()) return;

            try {
                await resoLinkWebSocket.Send(data);
            } catch (Exception e) {
                Debug.LogError($"Failed to send data to ResoLink: {e.Message}");
            }
        }

        public async Task<object> ReceiveAsync() {
            if (!IsConnected()) return null;

            try {
                return await resoLinkWebSocket.Receive();
            } catch (Exception e) {
                Debug.LogError($"Failed to receive data from ResoLink: {e.Message}");
                return null;
            }
        }
    }
}
