using System;
using System.Collections.Generic;
using System.Text;
using System.Net;

namespace WatsonWebsocket
{
    /// <summary>
    /// Event arguments for when a client disconnects from the server.
    /// </summary>
    public class ClientDisconnectedEventArgs : EventArgs
    {
        /// <summary>
        /// The sender client instance.
        /// </summary>
        public ClientMetadata Client { get; }

        internal ClientDisconnectedEventArgs(ClientMetadata client)
        {
            Client = client;
        }
    }
}