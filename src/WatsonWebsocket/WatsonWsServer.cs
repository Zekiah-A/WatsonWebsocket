using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text; 
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace WatsonWebsocket
{    
    /// <summary>
    /// Watson Websocket server.
    /// </summary>
    public class WatsonWsServer : IDisposable
    {
        /// <summary>
        /// Determine if the server is listening for new connections.
        /// </summary>
        public bool IsListening = false; //{ get; set; } = false; //listener is { IsListening: true };

        /// <summary>
        /// Enable or disable statistics.
        /// </summary>
        public bool EnableStatistics { get; set; } = true;

        /// <summary>
        /// Event fired when a client connects.
        /// </summary>
        public event EventHandler<ClientConnectedEventArgs>? ClientConnected;

        /// <summary>
        /// Event fired when a client disconnects.
        /// </summary>
        public event EventHandler<ClientDisconnectedEventArgs>? ClientDisconnected;

        /// <summary>
        /// Event fired when the server stops.
        /// </summary>
        public event EventHandler? ServerStopped;

        /// <summary>
        /// Event fired when a message is received.
        /// </summary>
        public event EventHandler<MessageReceivedEventArgs>? MessageReceived;

        /// <summary>
        /// Specify the IP addresses that are allowed to connect.  If none are supplied, all IP addresses are permitted.
        /// </summary>
        public List<string> PermittedIpAddresses { get; set; } = new();

        /// <summary>
        /// Method to invoke when sending a log message.
        /// </summary>
        public Action<string>? Logger;

        /// <summary>
        /// Statistics.
        /// </summary>
        public Statistics? Stats { get; set; }

        /// <summary>
        /// All clients currently connected to this server
        /// </summary>
        public List<ClientMetadata> Clients { get; set; }

        private const string Header = "[WatsonWsServer] ";
        private readonly List<string> listenerPrefixes = new();
        private CancellationTokenSource tokenSource;
        private CancellationToken token; 
        private IHost listener;

        /// <summary>
        /// Initializes the Watson websocket server with one or more listener prefixes.  
        /// Be sure to call 'Start()' to start the server.
        /// By default, Watson Websocket will listen on http://localhost:9000/.
        /// </summary>
        /// <param name="hostnames">The hostnames or IP addresses upon which to listen.</param>
        /// <param name="port">The TCP port on which to listen.</param>
        /// <param name="ssl">Enable or disable SSL.</param>
        public WatsonWsServer(int port = 9000, bool ssl = false, params string[] hostnames)
        {
            if (port < 0) throw new ArgumentOutOfRangeException(nameof(port));

            foreach (var hostname in hostnames)
            {
                if (ssl) listenerPrefixes.Add("https://" + hostname + ":" + port + "/");
                else listenerPrefixes.Add("http://" + hostname + ":" + port + "/");
            }

            listener = Host.CreateDefaultBuilder()
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseUrls(listenerPrefixes.ToArray());
                    webBuilder.Configure(app =>
                    {
                        app.UseWebSockets();
                        app.Use(AcceptConnectionsAsync);
                    });
                })
                .ConfigureLogging(logging => logging.SetMinimumLevel(LogLevel.Critical))
                .Build();
            
            tokenSource = new CancellationTokenSource();
            token = tokenSource.Token;
            Clients = new List<ClientMetadata>();
        }

        /// <summary>
        /// Initialises watson websocket server with a listener URI.
        /// Be sure to call 'Start()' to start the server.
        /// By default, Watson Websocket will listen on http://localhost:9000/.
        /// </summary>
        /// <param name="uri">URI which socket will listen upon.</param>
        public WatsonWsServer(Uri uri) : this(uri.Port, uri.Scheme is "wss" or "https", uri.Host) { }
        
        /// <summary>
        /// Tear down the server and dispose of background workers.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
        }

        /// <summary>
        /// Start accepting new connections.
        /// </summary>
        public void Start()
        {
            InitialiseListener();
            listener.Start();
        }

        /// <summary>
        /// Start accepting new connections.
        /// </summary>
        /// <returns>Task.</returns>
        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            InitialiseListener();
            listener.StartAsync(cancellationToken);
            return Task.Delay(1);
        }
        
        /// <summary>
        ///  Used by start and StartAsync to initialise the listener before starting the server. 
        /// </summary>
        private void InitialiseListener()
        {
            if (IsListening) throw new InvalidOperationException("Watson websocket server is already running.");

            Stats = new Statistics();

            var logMsg = listenerPrefixes.Aggregate(Header + "starting on:", (current, prefix) => current + " " + prefix);
            Logger?.Invoke(logMsg);
            
            tokenSource = new CancellationTokenSource();
            token = tokenSource.Token;
        }

        /// <summary>
        /// Stop accepting new connections.
        /// </summary>
        public void Stop()
        {
            if (!IsListening) throw new InvalidOperationException("Watson websocket server is not running.");

            Logger?.Invoke(Header + "stopping");

            listener.StopAsync(); 
        }

        /// <summary>
        /// Send text data to the specified client, asynchronously.
        /// </summary>
        /// <param name="client">The recipient client.</param>
        /// <param name="data">String containing data.</param>
        /// <param name="cancellationToken">Cancellation token allowing for termination of this request.</param>
        /// <returns>Task with Boolean indicating if the message was sent successfully.</returns>
        public Task<bool> SendAsync(ClientMetadata client, string data, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(data)) throw new ArgumentNullException(nameof(data));
            return MessageWriteAsync(client, new ArraySegment<byte>(Encoding.UTF8.GetBytes(data)), WebSocketMessageType.Text, cancellationToken);
        }

        /// <summary>
        /// Send binary data to the specified client, asynchronously.
        /// </summary>
        /// <param name="client">The recipient client.</param>
        /// <param name="data">Byte array containing data.</param> 
        /// <param name="cancellationToken">Cancellation token allowing for termination of this request.</param>
        /// <returns>Task with Boolean indicating if the message was sent successfully.</returns>
        public Task<bool> SendAsync(ClientMetadata client, byte[] data, CancellationToken cancellationToken = default)
        {
            return SendAsync(client, new ArraySegment<byte>(data), WebSocketMessageType.Binary, cancellationToken);
        }

        /// <summary>
        /// Send binary data to the specified client, asynchronously.
        /// </summary>
        /// <param name="client">The recipient client.</param>
        /// <param name="data">Byte array containing data.</param> 
        /// <param name="msgType">Web socket message type.</param>
        /// <param name="cancellationToken">Cancellation token allowing for termination of this request.</param>
        /// <returns>Task with Boolean indicating if the message was sent successfully.</returns>
        public Task<bool> SendAsync(ClientMetadata client, byte[] data, WebSocketMessageType msgType, CancellationToken cancellationToken = default)
        {
            return SendAsync(client, new ArraySegment<byte>(data), msgType, cancellationToken);
        }

        /// <summary>
        /// Send binary data to the specified client, asynchronously.
        /// </summary>
        /// <param name="client">The recipient client.</param>
        /// <param name="data">ArraySegment containing data.</param> 
        /// <param name="msgType">Web socket message type.</param>
        /// <param name="cancellationToken">Cancellation token allowing for termination of this request.</param>
        /// <returns>Task with Boolean indicating if the message was sent successfully.</returns>
        public Task<bool> SendAsync(ClientMetadata client, ArraySegment<byte> data, WebSocketMessageType msgType = WebSocketMessageType.Binary, CancellationToken cancellationToken = default)
        {
            if (data.Array == null || data.Count < 1) throw new ArgumentNullException(nameof(data));
            return MessageWriteAsync(client, data, msgType, cancellationToken);
        }

        /// <summary>
        /// Forcefully disconnect a client.
        /// </summary>
        /// <param name="client">The client being disconnected.</param>
        public void DisconnectClient(ClientMetadata client)
        {
            client.Ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", client.TokenSource.Token).Wait();
            client.TokenSource.Cancel();
            client.Ws.Dispose();
        }
        
        /// <summary>
        /// Gets the instance of a connected client from it's ipPort property.
        /// </summary>
        /// <param name="ipPort"></param>
        /// <returns></returns>
        public ClientMetadata? GetClientFromIpPort(string? ipPort)
        {
            return Clients.FirstOrDefault(target => target.IpPort == ipPort);
        }
        
        /// <summary>
        /// Tear down the server and dispose of background workers.
        /// </summary>
        /// <param name="disposing">Disposing.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!disposing) return;
            foreach (var client in Clients)
            {
                client.Ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", client.TokenSource.Token);
                client.TokenSource.Cancel();
            }

            listener.StopAsync();
            listener.Dispose();
            tokenSource.Cancel();
        }
        
        private async Task AcceptConnectionsAsync(HttpContext context, RequestDelegate next)
        {
            try
            {
                while (true) //TODO: Put cancellation token here instead + listener is listening
                {
                    var ip = context.Connection.RemoteIpAddress!.ToString();
                    var port = context.Connection.RemotePort.ToString();
                    var ipPort = ip + ":" + port;

                    if (PermittedIpAddresses is {Count: > 0} && !PermittedIpAddresses.Contains(ip))
                    {
                        Logger?.Invoke(Header + "rejecting " + ipPort + " (not permitted)");
                        context.Response.StatusCode = 401;
                        context.Connection.RequestClose();
                        continue;
                    }

                    if (!context.WebSockets.IsWebSocketRequest)
                    {
                        Logger?.Invoke(Header + "non-websocket request rejected from " + ipPort);
                        context.Response.StatusCode = 400;
                        context.Connection.RequestClose();
                        continue;
                    }

                    Logger?.Invoke(Header + "starting data receiver for " + ipPort);

                    var ws = await context.WebSockets.AcceptWebSocketAsync();
                    var md = new ClientMetadata(context, ws, tokenSource);
                    Clients.Add(md);

#pragma warning disable CS4014
                    Task.Run(() => ClientConnected?.Invoke(this, new ClientConnectedEventArgs(md, context.Request)))
                        .ConfigureAwait(false);
#pragma warning restore CS4014
                    await DataReceiver(md);
                }
            }
            catch (Exception e)
            {
                if (e is HttpListenerException or TaskCanceledException or OperationCanceledException or ObjectDisposedException)
                {
                    return;
                }

                Logger?.Invoke(Header + "listener exception:" + Environment.NewLine + e);
            }
            finally
            {
                ServerStopped?.Invoke(this, EventArgs.Empty);
            }
        }
        
        private async Task DataReceiver(ClientMetadata md)
        { 
            var header = "[WatsonWsServer " + md.IpPort + "] ";
            Logger?.Invoke(header + "starting data receiver");
            var buffer = new byte[65536];

            try
            {
                
                while (true)
                {
                    var msg = await MessageReadAsync(md, buffer).ConfigureAwait(false);
                    
                    if (EnableStatistics)
                    {
                        Stats?.IncrementReceivedMessages();
                        Stats?.AddReceivedBytes(msg.Data.Count);
                    }

#pragma warning disable CS4014
                    Task.Run(() => MessageReceived?.Invoke(this, msg), md.TokenSource.Token)
                        .ConfigureAwait(false);
#pragma warning restore CS4014
                }
            }
            catch (Exception e)
            {
                if (e is TaskCanceledException or OperationCanceledException or WebSocketException)
                {
                    return;
                }
                
                Logger?.Invoke(header + "exception: " + Environment.NewLine + e);
            }
            finally
            { 
#pragma warning disable CS4014
                Task.Run(() => ClientDisconnected?.Invoke(this, new ClientDisconnectedEventArgs(md)))
                    .ConfigureAwait(false);
#pragma warning restore CS4014
                
                md.Ws.Dispose();
                Logger?.Invoke(header + "disconnected");
                Clients.Remove(md);
            }
        }
         
        private async Task<MessageReceivedEventArgs> MessageReadAsync(ClientMetadata md, byte[] buffer)
        {
            var header = "[WatsonWsServer " + md.IpPort + "] ";

            using var ms = new MemoryStream();
            var seg = new ArraySegment<byte>(buffer);

            while (true)
            {
                var result = await md.Ws.ReceiveAsync(seg, md.TokenSource.Token).ConfigureAwait(false);
                if (result.CloseStatus != null)
                {
                    Logger?.Invoke(header + "close received");
                    await md.Ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                    throw new WebSocketException("Websocket closed.");
                }

                if (md.Ws.State != WebSocketState.Open)
                {
                    Logger?.Invoke(header + "websocket no longer open");
                    throw new WebSocketException("Websocket closed.");
                }

                if (md.TokenSource.Token.IsCancellationRequested)
                {
                    Logger?.Invoke(header + "cancel requested");
                }

                if (result.Count > 0)
                {
                    ms.Write(buffer, 0, result.Count);
                }

                if (result.EndOfMessage)
                {
                    return new MessageReceivedEventArgs(md, new ArraySegment<byte>(ms.GetBuffer(), 0, (int)ms.Length), result.MessageType);
                }
            }
        }
 
        private async Task<bool> MessageWriteAsync(ClientMetadata md, ArraySegment<byte> data, WebSocketMessageType msgType, CancellationToken cancellationToken)
        {
            var header = "[WatsonWsServer " + md.IpPort + "] ";

            var tokens = new CancellationToken[3];
            tokens[0] = token;
            tokens[1] = cancellationToken;
            tokens[2] = md.TokenSource.Token;

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(tokens);
            try
            {
                await md.SendLock.WaitAsync(md.TokenSource.Token).ConfigureAwait(false);

                try
                {
                    await md.Ws.SendAsync(data, msgType, true, linkedCts.Token).ConfigureAwait(false);
                }
                finally
                {
                    md.SendLock.Release();
                }

                if (EnableStatistics)
                {
                    Stats?.IncrementSentMessages();
                    Stats?.AddSentBytes(data.Count);
                }

                return true;
            }
            catch (TaskCanceledException)
            {
                if (this.token.IsCancellationRequested)
                {
                    Logger?.Invoke(header + "server canceled");
                }
                else if (cancellationToken.IsCancellationRequested)
                {
                    Logger?.Invoke(header + "message send canceled");
                }
                else if (md.TokenSource.Token.IsCancellationRequested)
                {
                    Logger?.Invoke(header + "client canceled");
                }
            }
            catch (OperationCanceledException)
            {
                if (this.token.IsCancellationRequested)
                {
                    Logger?.Invoke(header + "canceled");
                }
                else if (cancellationToken.IsCancellationRequested)
                {
                    Logger?.Invoke(header + "message send canceled");
                }
                else if (md.TokenSource.Token.IsCancellationRequested)
                {
                    Logger?.Invoke(header + "client canceled");
                }
            }
            catch (ObjectDisposedException)
            {
                Logger?.Invoke(header + "disposed");
            }
            catch (WebSocketException)
            {
                Logger?.Invoke(header + "websocket disconnected");
            }
            catch (SocketException)
            {
                Logger?.Invoke(header + "socket disconnected");
            }
            catch (InvalidOperationException)
            {
                Logger?.Invoke(header + "disconnected due to invalid operation");
            }
            catch (IOException)
            {
                Logger?.Invoke(header + "IO disconnected");
            }
            catch (Exception e)
            {
                Logger?.Invoke(header + "exception: " + Environment.NewLine + e);
            }

            return false;
        }
    }
}
