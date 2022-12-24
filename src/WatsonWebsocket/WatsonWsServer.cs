using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography.X509Certificates;
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
        /// Enable or disable statistics.
        /// </summary>
        public bool EnableStatistics { get; set; } = false;

        /// <summary>
        /// Event fired when a client connects.
        /// </summary>
        public event EventHandler<ClientConnectedEventArgs>? ClientConnected = (_, _) => {};

        /// <summary>
        /// Event fired when a client disconnects.
        /// </summary>
        public event EventHandler<ClientDisconnectedEventArgs>? ClientDisconnected = (_, _) => {};
        
        /// <summary>
        /// Event fired when a message is received.
        /// </summary>
        public event EventHandler<MessageReceivedEventArgs>? MessageReceived = (_, _) => {};

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
        private CancellationToken cancellationToken; 
        private IHost listener;

        /// <summary>
        /// Initializes the Watson websocket server with one or more listener prefixes.  
        /// Be sure to call 'Start()' to start the server.
        /// By default, Watson Websocket will listen on http://localhost:9000/.
        /// </summary>
        /// <param name="logLevel">Level of console logging done by the kestrel HTTP</param>
        /// <param name="hostnames">The hostnames or IP addresses upon which to listen.</param>
        /// <param name="port">The TCP port on which to listen.</param>
        /// <param name="ssl">Enable or disable SSL.</param>
        /// <param name="certificatePath">Path to PEM certificate file</param>
        /// <param name="keyPath">Path to PEM key file</param>
        public WatsonWsServer(int port = 9000, bool ssl = false, string? certificatePath = default,
                                string? keyPath = default, LogLevel? logLevel = null, params string[] hostnames)
        {
            if (port < 0) throw new ArgumentOutOfRangeException(nameof(port));

            foreach (var hostname in hostnames)
            {
                if (ssl) listenerPrefixes.Add("https://" + hostname + ":" + port + "/");
                else listenerPrefixes.Add("http://" + hostname + ":" + port + "/");
            }

            X509Certificate2? x509 = null;
            if (ssl && Path.Exists(certificatePath) && Path.Exists(keyPath))
            {
                var certPem = File.ReadAllText(certificatePath);
                var keyPem = File.ReadAllText(keyPath);
                x509 = X509Certificate2.CreateFromPem(certPem, keyPem);
            }

            listener = Host.CreateDefaultBuilder()
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseUrls(listenerPrefixes.ToArray());
                    
                    webBuilder.UseKestrel(options =>
                    {
                        if (!ssl || x509 is null) return;
                        
                        foreach (var prefix in listenerPrefixes)
                        {
                            options.Listen(IPEndPoint.Parse(prefix), listenOptions =>
                                listenOptions.UseHttps(x509)
                            );
                        }
                    });

                    webBuilder.Configure(app =>
                    {
                        app.UseWebSockets();
                        app.Use(AcceptConnectionsAsync);
                    });
                })
                .ConfigureLogging(logging => logging.SetMinimumLevel(logLevel ?? LogLevel.None))
                .Build();
            
            tokenSource = new CancellationTokenSource();
            Clients = new List<ClientMetadata>();
            cancellationToken = tokenSource.Token;
        }

        /// <summary>
        /// Initialises watson websocket server with a listener URI.
        /// Be sure to call 'Start()' to start the server.
        /// By default, Watson Websocket will listen on http://localhost:9000/.
        /// </summary>
        /// <param name="uri">URI which socket will listen upon.</param>
        public WatsonWsServer(Uri uri) : this(uri.Port, uri.Scheme is "wss" or "https", null, null, null, uri.Host) { }
        
        /// <summary>
        /// Initialises watson websocket server with a listener port and hostname.
        /// Be sure to call 'Start()' to start the server.
        /// By default, Watson Websocket will listen on http://localhost:9000/.
        /// </summary>
        /// <param name="port">The TCP port on which to listen.</param>
        /// <param name="hostname">The hostnames or IP addresses upon which to listen.</param>
        public WatsonWsServer(int port, params string[] hostname) : this(port, false, null, null, null, hostname) { }

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
        public Task StartAsync(CancellationToken token = default)
        {
            InitialiseListener();
            listener.StartAsync(token);
            return Task.Delay(1, token);
        }
        
        /// <summary>
        ///  Used by start and StartAsync to initialise the listener before starting the server. 
        /// </summary>
        private void InitialiseListener()
        {
            Stats = new Statistics();

            var logMsg = listenerPrefixes.Aggregate(Header + "starting on:", (current, prefix) => current + " " + prefix);
            Logger?.Invoke(logMsg);
            
            tokenSource = new CancellationTokenSource();
            cancellationToken = tokenSource.Token;
        }

        /// <summary>
        /// Stop accepting new connections.
        /// </summary>
        public async Task StopAsync(CancellationToken token = default)
        {
            Logger?.Invoke(Header + "stopping");
            await listener.StopAsync(token);
        }

        /// <summary>
        /// Send text data to the specified client, asynchronously.
        /// </summary>
        /// <param name="client">The recipient client.</param>
        /// <param name="data">String containing data.</param>
        /// <param name="token">Cancellation cancellationToken allowing for termination of this request.</param>
        /// <returns>Task with Boolean indicating if the message was sent successfully.</returns>
        public Task<bool> SendAsync(ClientMetadata client, string data, CancellationToken token = default)
        {
            return MessageWriteAsync(client, new ArraySegment<byte>(Encoding.UTF8.GetBytes(data)), WebSocketMessageType.Text, token);
        }

        /// <summary>
        /// Send binary data to the specified client, asynchronously.
        /// </summary>
        /// <param name="client">The recipient client.</param>
        /// <param name="data">Byte array containing data.</param> 
        /// <param name="token">Cancellation cancellationToken allowing for termination of this request.</param>
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
        /// <param name="token">Cancellation cancellationToken allowing for termination of this request.</param>
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
        /// <param name="token">Cancellation cancellationToken allowing for termination of this request.</param>
        /// <returns>Task with Boolean indicating if the message was sent successfully.</returns>
        public Task<bool> SendAsync(ClientMetadata client, ArraySegment<byte> data,
            WebSocketMessageType msgType = WebSocketMessageType.Binary, CancellationToken token = default)
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
                var webSocket = await context.WebSockets.AcceptWebSocketAsync();

                var ip = context.Connection.RemoteIpAddress!.ToString();
                var port = context.Connection.RemotePort.ToString();
                var ipPort = ip + ":" + port;

                if (PermittedIpAddresses is { Count: > 0 } && !PermittedIpAddresses.Contains(ip))
                {
                    Logger?.Invoke(Header + "rejecting " + ipPort + " (not permitted)");
                    context.Response.StatusCode = 401;
                    context.Connection.RequestClose();
                    return; 
                }

                if (!context.WebSockets.IsWebSocketRequest)
                {
                    Logger?.Invoke(Header + "non-websocket request rejected from " + ipPort);
                    context.Response.StatusCode = 400;
                    context.Connection.RequestClose();
                    return;
                }

                Logger?.Invoke(Header + "starting data receiver for " + ipPort);
                
                var metadata = new ClientMetadata(context, webSocket, tokenSource);
                Clients.Add(metadata);

                ClientConnected?.Invoke(this, new ClientConnectedEventArgs(metadata, context.Request));
                await DataReceiver(metadata);
            }
            catch (Exception exception)
            {
                if (exception is HttpListenerException or TaskCanceledException or OperationCanceledException or ObjectDisposedException)
                {
                    return;
                }

                Logger?.Invoke(Header + "listener exception:" + Environment.NewLine + exception);
            }
        }
        
        private async Task DataReceiver(ClientMetadata metadata)
        { 
            var header = "[WatsonWsServer " + metadata.IpPort + "] ";
            Logger?.Invoke(header + "starting data receiver");
            var buffer = new byte[65536];

            try
            {
                
                while (true)
                {
                    var msg = await MessageReadAsync(metadata, buffer).ConfigureAwait(false);
                    
                    if (EnableStatistics)
                    {
                        Stats?.IncrementReceivedMessages();
                        Stats?.AddReceivedBytes(msg.Data.Count);
                    }

                    MessageReceived?.Invoke(this, msg);
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
                metadata.Ws.Dispose();
                Logger?.Invoke(header + "disconnected");
                Clients.Remove(metadata);

                ClientDisconnected?.Invoke(this, new ClientDisconnectedEventArgs(metadata));
            }
        }
         
        private async Task<MessageReceivedEventArgs> MessageReadAsync(ClientMetadata metadata, byte[] buffer)
        {
            var header = "[WatsonWsServer " + metadata.IpPort + "] ";

            using var ms = new MemoryStream();
            var seg = new ArraySegment<byte>(buffer);

            while (true)
            {
                var result = await metadata.Ws.ReceiveAsync(seg, metadata.TokenSource.Token);
                if (result.CloseStatus != null)
                {
                    Logger?.Invoke(header + "close received");
                    await metadata.Ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                    throw new WebSocketException("Websocket closed.");
                }

                if (metadata.Ws.State != WebSocketState.Open)
                {
                    Logger?.Invoke(header + "websocket no longer open");
                    throw new WebSocketException("Websocket closed.");
                }

                if (metadata.TokenSource.Token.IsCancellationRequested)
                {
                    Logger?.Invoke(header + "cancel requested");
                }

                if (result.Count > 0)
                {
                    ms.Write(buffer, 0, result.Count);
                }

                if (result.EndOfMessage)
                {
                    return new MessageReceivedEventArgs(metadata, new ArraySegment<byte>(ms.GetBuffer(), 0, (int)ms.Length), result.MessageType);
                }
            }
        }
 
        private async Task<bool> MessageWriteAsync(ClientMetadata metadata, ArraySegment<byte> data, WebSocketMessageType msgType, CancellationToken token)
        {
            var header = "[WatsonWsServer " + metadata.IpPort + "] ";

            var tokens = new CancellationToken[3];
            tokens[0] = cancellationToken;
            tokens[1] = token;
            tokens[2] = metadata.TokenSource.Token;

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(tokens);
            try
            {
                await metadata.SendLock.WaitAsync(metadata.TokenSource.Token).ConfigureAwait(false);

                try
                {
                    await metadata.Ws.SendAsync(data, msgType, true, linkedCts.Token).ConfigureAwait(false);
                }
                finally
                {
                    metadata.SendLock.Release();
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
                if (cancellationToken.IsCancellationRequested)
                {
                    Logger?.Invoke(header + "server canceled");
                }
                else if (token.IsCancellationRequested)
                {
                    Logger?.Invoke(header + "message send canceled");
                }
                else if (metadata.TokenSource.Token.IsCancellationRequested)
                {
                    Logger?.Invoke(header + "client canceled");
                }
            }
            catch (OperationCanceledException)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    Logger?.Invoke(header + "canceled");
                }
                else if (token.IsCancellationRequested)
                {
                    Logger?.Invoke(header + "message send canceled");
                }
                else if (metadata.TokenSource.Token.IsCancellationRequested)
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
