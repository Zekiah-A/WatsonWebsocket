using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text; 
using System.Threading;
using System.Threading.Tasks;
 
namespace WatsonWebsocket
{    
    /// <summary>
    /// Watson Websocket server.
    /// </summary>
    public class WatsonWsServer : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Determine if the server is listening for new connections.
        /// </summary>
        public bool IsListening => Listener is {IsListening: true};

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
        /// Indicate whether or not invalid or otherwise unverifiable certificates should be accepted.  Default is true.
        /// </summary>
        public bool AcceptInvalidCertificates { get; set; }
        /// <summary>
        /// Specify the IP addresses that are allowed to connect.  If none are supplied, all IP addresses are permitted.
        /// </summary>
        public List<string> PermittedIpAddresses { get; set; } = new();

        /// <summary>
        /// Method to invoke when sending a log message.
        /// </summary>
        public Action<string>? Logger;

        /// <summary>
        /// Method to invoke when receiving a raw (non-websocket) HTTP request.
        /// </summary>
        public Action<HttpListenerContext>? HttpHandler;

        /// <summary>
        /// Statistics.
        /// </summary>
        public Statistics? Stats { get; set; }

        /// <summary>
        /// All clients currently connected to this server
        /// </summary>
        public List<ClientMetadata> Clients { get; set; }
        #endregion

        #region Private-Members

        private readonly string Header = "[WatsonWsServer] ";
        private readonly List<string> ListenerPrefixes = new();
        private readonly HttpListener Listener;
        private CancellationTokenSource TokenSource;
        private CancellationToken Token; 
        private Task? AcceptConnectionsTask;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes the Watson websocket server with a single listener prefix.
        /// Be sure to call 'Start()' to start the server.
        /// By default, Watson Websocket will listen on http://localhost:9000/.
        /// </summary>
        /// <param name="hostname">The hostname or IP address upon which to listen.</param>
        /// <param name="port">The TCP port on which to listen.</param>
        /// <param name="ssl">Enable or disable SSL.</param> 
        public WatsonWsServer(string hostname = "localhost", int port = 9000, bool ssl = false)
        {
            if (port < 0) throw new ArgumentOutOfRangeException(nameof(port));
            if (string.IsNullOrEmpty(hostname)) hostname = "localhost";
            
            if (ssl) ListenerPrefixes.Add("https://" + hostname + ":" + port + "/");
            else ListenerPrefixes.Add("http://" + hostname + ":" + port + "/");

            Listener = new HttpListener();
            foreach (var prefix in ListenerPrefixes)
            {
                Listener.Prefixes.Add(prefix);
            }

            TokenSource = new CancellationTokenSource();
            Token = TokenSource.Token;
            Clients = new List<ClientMetadata>();
        }

        /// <summary>
        /// Initializes the Watson websocket server with one or more listener prefixes.  
        /// Be sure to call 'Start()' to start the server.
        /// </summary>
        /// <param name="hostnames">The hostnames or IP addresses upon which to listen.</param>
        /// <param name="port">The TCP port on which to listen.</param>
        /// <param name="ssl">Enable or disable SSL.</param>
        public WatsonWsServer(List<string> hostnames, int port, bool ssl = false)
        {
            if (port < 0) throw new ArgumentOutOfRangeException(nameof(port));
            if (hostnames == null) throw new ArgumentNullException(nameof(hostnames));
            if (hostnames.Count < 1) throw new ArgumentException("At least one hostname must be supplied.");

            foreach (var hostname in hostnames)
            {
                if (ssl) ListenerPrefixes.Add("https://" + hostname + ":" + port + "/");
                else ListenerPrefixes.Add("http://" + hostname + ":" + port + "/");
            }

            Listener = new HttpListener();
            foreach (var prefix in ListenerPrefixes)
            {
                Listener.Prefixes.Add(prefix);
            }

            TokenSource = new CancellationTokenSource();
            Token = TokenSource.Token;
            Clients = new List<ClientMetadata>();
        }

        /// <summary>
        /// Initializes the Watson websocket server.
        /// Be sure to call 'Start()' to start the server.
        /// </summary>
        /// <param name="uri">The URI on which you wish to listen, i.e. http://localhost:9090.</param>
        public WatsonWsServer(Uri uri)
        {
            if (uri == null) throw new ArgumentNullException(nameof(uri));

            if (uri.Port < 0) throw new ArgumentException("Port must be zero or greater.");

            string host;
            if (!IPAddress.TryParse(uri.Host, out var _))
            {
                var dnsLookup = Dns.GetHostEntry(uri.Host);
                if (dnsLookup.AddressList.Length > 0)
                {
                    host = dnsLookup.AddressList.First().ToString();
                }
                else
                {
                    throw new ArgumentException("Cannot resolve address to IP.");
                }
            }
            else
            {
                host = uri.Host;
            }

            var listenerUri = new UriBuilder(uri)
            {
                Host = host
            };

            ListenerPrefixes.Add(listenerUri.ToString());

            Listener = new HttpListener();
            foreach (var prefix in ListenerPrefixes)
            {
                Listener.Prefixes.Add(prefix);
            }

            TokenSource = new CancellationTokenSource();
            Token = TokenSource.Token;
            Clients = new List<ClientMetadata>();
        }

        #endregion

        #region Public-Methods

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
            if (IsListening) throw new InvalidOperationException("Watson websocket server is already running.");

            Stats = new Statistics();

            var logMsg = Header + "starting on:";
            foreach (var prefix in ListenerPrefixes) logMsg += " " + prefix;
            Logger?.Invoke(logMsg);

            if (AcceptInvalidCertificates) SetInvalidCertificateAcceptance();

            TokenSource = new CancellationTokenSource();
            Token = TokenSource.Token;
            Listener.Start();

            AcceptConnectionsTask = Task.Run(() => AcceptConnections(Token), Token);
        }

        /// <summary>
        /// Start accepting new connections.
        /// </summary>
        /// <returns>Task.</returns>
        public Task StartAsync(CancellationToken token = default)
        {
            if (IsListening) throw new InvalidOperationException("Watson websocket server is already running.");

            Stats = new Statistics();

            var logMsg = Header + "starting on:";
            foreach (var prefix in ListenerPrefixes) logMsg += " " + prefix;
            Logger?.Invoke(logMsg);

            if (AcceptInvalidCertificates) SetInvalidCertificateAcceptance();

            TokenSource = CancellationTokenSource.CreateLinkedTokenSource(token);
            Token = token;

            Listener.Start();

            AcceptConnectionsTask = Task.Run(() => AcceptConnections(Token), Token);

            return Task.Delay(1);
        }

        /// <summary>
        /// Stop accepting new connections.
        /// </summary>
        public void Stop()
        {
            if (!IsListening) throw new InvalidOperationException("Watson websocket server is not running.");

            Logger?.Invoke(Header + "stopping");

            Listener.Stop(); 
        }

        /// <summary>
        /// Send text data to the specified client, asynchronously.
        /// </summary>
        /// <param name="client">The recipient client.</param>
        /// <param name="data">String containing data.</param>
        /// <param name="token">Cancellation token allowing for termination of this request.</param>
        /// <returns>Task with Boolean indicating if the message was sent successfully.</returns>
        public Task<bool> SendAsync(ClientMetadata client, string data, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(data)) throw new ArgumentNullException(nameof(data));
            return MessageWriteAsync(client, new ArraySegment<byte>(Encoding.UTF8.GetBytes(data)), WebSocketMessageType.Text, token);
        }

        /// <summary>
        /// Send binary data to the specified client, asynchronously.
        /// </summary>
        /// <param name="client">The recipient client.</param>
        /// <param name="data">Byte array containing data.</param> 
        /// <param name="token">Cancellation token allowing for termination of this request.</param>
        /// <returns>Task with Boolean indicating if the message was sent successfully.</returns>
        public Task<bool> SendAsync(ClientMetadata client, byte[] data, CancellationToken token = default)
        {
            return SendAsync(client, new ArraySegment<byte>(data), WebSocketMessageType.Binary, token);
        }

        /// <summary>
        /// Send binary data to the specified client, asynchronously.
        /// </summary>
        /// <param name="client">The recipient client.</param>
        /// <param name="data">Byte array containing data.</param> 
        /// <param name="msgType">Web socket message type.</param>
        /// <param name="token">Cancellation token allowing for termination of this request.</param>
        /// <returns>Task with Boolean indicating if the message was sent successfully.</returns>
        public Task<bool> SendAsync(ClientMetadata client, byte[] data, WebSocketMessageType msgType, CancellationToken token = default)
        {
            return SendAsync(client, new ArraySegment<byte>(data), msgType, token);
        }

        /// <summary>
        /// Send binary data to the specified client, asynchronously.
        /// </summary>
        /// <param name="client">The recipient client.</param>
        /// <param name="data">ArraySegment containing data.</param> 
        /// <param name="msgType">Web socket message type.</param>
        /// <param name="token">Cancellation token allowing for termination of this request.</param>
        /// <returns>Task with Boolean indicating if the message was sent successfully.</returns>
        public Task<bool> SendAsync(ClientMetadata client, ArraySegment<byte> data, WebSocketMessageType msgType = WebSocketMessageType.Binary, CancellationToken token = default)
        {
            if (data.Array == null || data.Count < 1) throw new ArgumentNullException(nameof(data));
            return MessageWriteAsync(client, data, msgType, token);
        }

        /// <summary>
        /// Forcefully disconnect a client.
        /// </summary>
        /// <param name="client">The client being disconnected.</param>
        public void DisconnectClient(ClientMetadata client)
        {
            //TODO: Do we need an alternative to lock here?
            // lock because CloseOutputAsync can fail with InvalidOperationAsync with overlapping operations
            client.Ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", client.TokenSource.Token).Wait();
            client.TokenSource.Cancel();
            client.Ws.Dispose();
        }
        
        /// <summary>
        /// Gets the instance of a connected client from it's ipPort property.
        /// </summary>
        /// <param name="ipPort"></param>
        /// <returns></returns>
        public ClientMetadata? GetClientFromIpPort(string ipPort)
        {
            return Clients.FirstOrDefault(target => target.IpPort == ipPort);
        }

        /// <summary>
        /// Retrieve the awaiter.
        /// </summary>
        /// <returns>TaskAwaiter.</returns>
        public TaskAwaiter? GetAwaiter()
        {
            return AcceptConnectionsTask?.GetAwaiter();
        }

        #endregion

        #region Private-Methods

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

            if (Listener.IsListening) Listener.Stop();
            Listener.Close();

            TokenSource.Cancel();
        }

        private static void SetInvalidCertificateAcceptance()
        {
            ServicePointManager.ServerCertificateValidationCallback += (sender, certificate, chain, sslPolicyErrors) => true;
        }

        private async Task AcceptConnections(CancellationToken cancelToken)
        { 
            try
            { 
                while (!cancelToken.IsCancellationRequested)
                {
                    if (!Listener.IsListening)
                    {
                        Task.Delay(100).Wait();
                        continue;
                    } 

                    var ctx = await Listener.GetContextAsync().ConfigureAwait(false);
                    var ip = ctx.Request.RemoteEndPoint.Address.ToString();
                    var port = ctx.Request.RemoteEndPoint.Port;
                    var ipPort = ip + ":" + port;

                    if (PermittedIpAddresses is {Count: > 0} && !PermittedIpAddresses.Contains(ip))
                    {
                        Logger?.Invoke(Header + "rejecting " + ipPort + " (not permitted)");
                        ctx.Response.StatusCode = 401;
                        ctx.Response.Close();
                        continue;
                    }

                    if (!ctx.Request.IsWebSocketRequest)
                    {
                        if (HttpHandler == null)
                        {
                            Logger?.Invoke(Header + "non-websocket request rejected from " + ipPort);
                            ctx.Response.StatusCode = 400;
                            ctx.Response.Close();
                        }
                        else
                        {
                            Logger?.Invoke(Header + "non-websocket request from " + ipPort + " HTTP-forwarded: " + ctx.Request.HttpMethod + " " + ctx.Request.RawUrl);
                            HttpHandler.Invoke(ctx);
                        }
                        
                        continue;
                    } 
                    else
                    { 
                        /*
                        HttpListenerRequest req = ctx.Request;
                        Console.WriteLine(Environment.NewLine + req.HttpMethod.ToString() + " " + req.RawUrl);
                        if (req.Headers != null && req.Headers.Count > 0)
                        {
                            Console.WriteLine("Headers:");
                            var items = req.Headers.AllKeys.SelectMany(req.Headers.GetValues, (k, v) => new { key = k, value = v });
                            foreach (var item in items)
                            {
                                Console.WriteLine("  {0}: {1}", item.key, item.value);
                            }
                        } 
                        */
                    }

                    await Task.Run(() =>
                    {
                        Logger?.Invoke(Header + "starting data receiver for " + ipPort);

                        var tokenSource = new CancellationTokenSource();
                        var token = tokenSource.Token;

                        Task.Run(async () =>
                        {
                            WebSocketContext wsContext = await ctx.AcceptWebSocketAsync(subProtocol: null);
                            var ws = wsContext.WebSocket;
                            var md = new ClientMetadata(ctx, ws, wsContext, tokenSource);
                             
                            Clients.Add(md);

                            ClientConnected?.Invoke(this, new ClientConnectedEventArgs(md, ctx.Request));
                            await Task.Run(() => DataReceiver(md), token);
                             
                        }, token);

                    }, Token).ConfigureAwait(false); 
                } 
            }
            /*
            catch (HttpListenerException)
            {
                // thrown when disposed
            }
            */
            catch (TaskCanceledException)
            {
                // thrown when disposed
            }
            catch (OperationCanceledException)
            {
                // thrown when disposed
            }
            catch (ObjectDisposedException)
            {
                // thrown when disposed
            }
            catch (Exception e)
            {
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
                    Task.Run(() => MessageReceived?.Invoke(this, msg), md.TokenSource.Token);
#pragma warning restore CS4014
                }
            }  
            catch (TaskCanceledException)
            {
                // thrown when disposed
            }
            catch (OperationCanceledException)
            {
                // thrown when disposed
            }
            catch (WebSocketException)
            {
                // thrown by MessageReadAsync
            } 
            catch (Exception e)
            { 
                Logger?.Invoke(header + "exception: " + Environment.NewLine + e);
            }
            finally
            { 
                ClientDisconnected?.Invoke(this, new ClientDisconnectedEventArgs(md));
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
 
        private async Task<bool> MessageWriteAsync(ClientMetadata md, ArraySegment<byte> data, WebSocketMessageType msgType, CancellationToken token)
        {
            var header = "[WatsonWsServer " + md.IpPort + "] ";

            var tokens = new CancellationToken[3];
            tokens[0] = Token;
            tokens[1] = token;
            tokens[2] = md.TokenSource.Token;

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(tokens);
            try
            {
                #region Send-Message

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

                #endregion
            }
            catch (TaskCanceledException)
            {
                if (Token.IsCancellationRequested)
                {
                    Logger?.Invoke(header + "server canceled");
                }
                else if (token.IsCancellationRequested)
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
                if (Token.IsCancellationRequested)
                {
                    Logger?.Invoke(header + "canceled");
                }
                else if (token.IsCancellationRequested)
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
         
        #endregion
    }
}
