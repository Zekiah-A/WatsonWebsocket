using System;
using System.Net.WebSockets;

namespace WatsonWebsocket;

public class ServerMessageReceivedEventArgs : MessageReceivedEventArgs
{
    #region Public-Members

    /// <summary>
    /// The sender client instance.
    /// </summary>
    public string IpPort { get; }
    #endregion

    #region Private-Members

    #endregion

    #region Constructors-and-Factories

    internal ServerMessageReceivedEventArgs(string ipPort, ArraySegment<byte> data, WebSocketMessageType messageType) : base(null, data, messageType)
    {
        IpPort = ipPort;
        Data = data;
        MessageType = messageType;
    }

    #endregion

    #region Public-Methods

    #endregion

    #region Private-Methods

    #endregion

}