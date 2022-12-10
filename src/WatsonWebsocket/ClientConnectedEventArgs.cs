using System;
using System.Collections.Generic;
using System.Text;
using System.Net;
using Microsoft.AspNetCore.Http;

namespace WatsonWebsocket
{
    /// <summary>
    /// Event arguments for when a client connects to the server.
    /// </summary>
    public class ClientConnectedEventArgs : EventArgs
    {
        /// <summary>
        /// The sender client instance.
        /// </summary>
        public ClientMetadata Client { get; }

        /// <summary>
        /// The HttpListenerRequest from the client.  Helpful for accessing HTTP request related metadata such as the querystring.
        /// </summary>
        public HttpRequest HttpRequest { get; }

        internal ClientConnectedEventArgs(ClientMetadata client, HttpRequest http)
        {
            Client = client;
            HttpRequest = http;
        }
    }
}