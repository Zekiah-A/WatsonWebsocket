﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Net;
using System.Net.WebSockets;

namespace WatsonWebsocket
{
    /// <summary>
    /// Event arguments for when a message is received.
    /// </summary>
    public class MessageReceivedEventArgs : EventArgs
    {
        /// <summary>
        /// The sender client instance.
        /// </summary>
        public ClientMetadata Client { get; }

        /// <summary>
        /// The data received.
        /// </summary>
        public ArraySegment<byte> Data { get; set; }

        /// <summary>
        /// The type of payload included in the message (Binary or Text).
        /// </summary>
        public WebSocketMessageType MessageType = WebSocketMessageType.Binary;

        internal MessageReceivedEventArgs(ClientMetadata client, ArraySegment<byte> data, WebSocketMessageType messageType)
        {
            Client = client;
            Data = data;
            MessageType = messageType;
        }
    }
}