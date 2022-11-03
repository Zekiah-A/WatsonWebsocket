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
        #region Public-Members

        /// <summary>
        /// The sender client instance.
        /// </summary>
        public ClientMetadata Client { get; }

        #endregion

        #region Private-Members

        #endregion

        #region Constructors-and-Factories

        internal ClientDisconnectedEventArgs(ClientMetadata client)
        {
            Client = client;
        }

        #endregion

        #region Public-Methods

        #endregion

        #region Private-Methods

        #endregion
    }
}