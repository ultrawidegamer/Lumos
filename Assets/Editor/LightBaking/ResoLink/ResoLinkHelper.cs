using UnityEditor;
using UnityEngine;
using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

public class ResoLinkHelper {
    public async Task ConnectAsync(string url) {
        await ResoLinkWebSocketSingleton.instance.Connect(url);
    }
    public async Task DisconnectAsync() {
        await ResoLinkWebSocketSingleton.instance.Disconnect();
    }

    public bool IsConnected() {
        return ResoLinkWebSocketSingleton.instance.IsConnected();
    }

    public async Task SendAsync<T>(T data) {
        await ResoLinkWebSocketSingleton.instance.Send(data);
    }

    public async Task ReceiveAsync() {
        Debug.Log(await ResoLinkWebSocketSingleton.instance.Receive());
    }
}
