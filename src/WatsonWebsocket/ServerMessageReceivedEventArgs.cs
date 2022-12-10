using System;
using System.Net.WebSockets;

namespace WatsonWebsocket;

public class ServerMessageReceivedEventArgs : MessageReceivedEventArgs
{
    /// <summary>
    /// The sender client instance.
    /// </summary>
    public string IpPort { get; }

    internal ServerMessageReceivedEventArgs(string ipPort, ArraySegment<byte> data, WebSocketMessageType messageType) : base(null, data, messageType)
    {
        IpPort = ipPort;
        Data = data;
        MessageType = messageType;
    }
}