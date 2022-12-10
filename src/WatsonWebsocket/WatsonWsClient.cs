using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace WatsonWebsocket
{
    /// <summary>
    /// Watson websocket client.
    /// </summary>
    public class WatsonWsClient : IDisposable
    {
        /// <summary>
        /// Indicate whether or not invalid or otherwise unverifiable certificates should be accepted.  Default is true.
        /// </summary>
        public bool AcceptInvalidCertificates { get; set; }
        /// <summary>
        /// Indicates whether or not the client is connected to the server.
        /// </summary>
        public bool Connected => clientWs.State == WebSocketState.Open;

        /// <summary>
        /// Enable or disable statistics.
        /// </summary>
        public bool EnableStatistics { get; set; } = true;

        /// <summary>
        /// Set KeepAlive to Connection Options.
        /// </summary>
        public int KeepAliveInterval
        {
            get => keepAliveIntervalSeconds;
            set
            {
                if (value < 1) throw new ArgumentException("ConnectTimeoutSeconds must be greater than zero.");
                keepAliveIntervalSeconds = value;
            }
        }
         
        /// <summary>
        /// Event fired when a message is received.
        /// </summary>
        public event EventHandler<MessageReceivedEventArgs>? MessageReceived;

        /// <summary>
        /// Event fired when the client connects successfully to the server. 
        /// </summary>
        public event EventHandler? ServerConnected;

        /// <summary>
        /// Event fired when the client disconnects from the server.
        /// </summary>
        public event EventHandler? ServerDisconnected;

        /// <summary>
        /// Method to invoke when sending a log message.
        /// </summary>
        public Action<string>? Logger = null;

        /// <summary>
        /// Statistics.
        /// </summary>
        public Statistics Stats { get; set; } = new();

        private const string Header = "[WatsonWsClient] ";
        private readonly Uri serverUri;
        private readonly string? serverIp;
        private readonly int serverPort;
        private readonly string serverIpPort;
        private ClientWebSocket clientWs;
        private int keepAliveIntervalSeconds = 30;
        private readonly CookieContainer cookies = new();
        private Action<ClientWebSocketOptions>? preConfigureOptions;

        private event EventHandler<MessageReceivedEventArgs>? AwaitingSyncResponseEvent;

        private readonly SemaphoreSlim sendLock = new(1);
        private readonly SemaphoreSlim awaitingSyncResponseLock = new(1);

        private readonly CancellationTokenSource tokenSource = new();
        private readonly CancellationToken token;

        /// <summary>
        /// Initializes the Watson websocket client.
        /// Be sure to call 'Start()' to start the client and connect to the server.
        /// </summary>
        /// <param name="serverIp">IP address of the server.</param>
        /// <param name="serverPort">TCP port of the server.</param>
        /// <param name="ssl">Enable or disable SSL.</param>
        public WatsonWsClient(string? serverIp, int serverPort, bool ssl)
        {
            string? url;
            if (string.IsNullOrEmpty(serverIp)) throw new ArgumentNullException(nameof(serverIp));
            if (serverPort < 1) throw new ArgumentOutOfRangeException(nameof(serverPort));

            this.serverIp = serverIp;
            this.serverPort = serverPort;
            serverIpPort = serverIp + ":" + serverPort;

            if (ssl) url = "wss://" + this.serverIp + ":" + this.serverPort;
            else url = "ws://" + this.serverIp + ":" + this.serverPort;
            serverUri = new Uri(url);
            token = tokenSource.Token;

            clientWs = new ClientWebSocket();
        }

        /// <summary>
        /// Initializes the Watson websocket client.
        /// Be sure to call 'Start()' to start the client and connect to the server.
        /// </summary>
        /// <param name="uri">The URI of the server endpoint.</param> 
        public WatsonWsClient(Uri uri)
        {
            serverUri = uri;
            serverIp = uri.Host;
            serverPort = uri.Port;
            serverIpPort = uri.Host + ":" + uri.Port;
            token = tokenSource.Token;

            clientWs = new ClientWebSocket();
        }

        /// <summary>
        /// Tear down the client and dispose of background workers.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Pre-configure websocket client options prior to connecting to the server.
        /// </summary>
        /// <returns>WatsonWsClient.</returns>
        public WatsonWsClient ConfigureOptions(Action<ClientWebSocketOptions>? options)
        {
            if (!Connected)
            {
                preConfigureOptions = options;
            }

            return this;
        }

        /// <summary>
        /// Add a cookie prior to connecting to the server.
        /// </summary>
        public void AddCookie(Cookie cookie) => cookies.Add(cookie);

        /// <summary>
        /// Start the client and connect to the server.
        /// </summary>
        public void Start()
        {
            Stats = new Statistics();
            if (AcceptInvalidCertificates) SetInvalidCertificateAcceptance();

            clientWs.Options.Cookies = cookies;
            clientWs.Options.KeepAliveInterval = TimeSpan.FromSeconds(keepAliveIntervalSeconds);

            preConfigureOptions?.Invoke(clientWs.Options);

            clientWs.ConnectAsync(serverUri, token).ContinueWith(AfterConnect).Wait();
        }

        /// <summary>
        /// Start the client and connect to the server.
        /// </summary>
        /// <returns>Task.</returns>
        public Task StartAsync()
        {
            Stats = new Statistics();
            if (AcceptInvalidCertificates) SetInvalidCertificateAcceptance();

            clientWs.Options.Cookies = cookies;
            clientWs.Options.KeepAliveInterval = TimeSpan.FromSeconds(keepAliveIntervalSeconds);

            if (preConfigureOptions != null)
            {
                preConfigureOptions(clientWs.Options);
            }

            // Connect
            return clientWs.ConnectAsync(serverUri, token).ContinueWith(AfterConnect);
        }

        /// <summary>
        /// Start the client and attempt to connect to the server until the timeout is reached.
        /// </summary>
        /// <param name="timeout">Timeout in seconds.</param>
        /// <param name="token">Cancellation token to terminate connection attempts.</param>
        /// <returns>Boolean indicating if the connection was successful.</returns>
        public bool StartWithTimeout(int timeout = 30, CancellationToken token = default)
        {
            if (timeout < 1) throw new ArgumentException("Timeout must be greater than zero seconds.");

            Stats = new Statistics();
            if (AcceptInvalidCertificates) SetInvalidCertificateAcceptance();

            var sw = new Stopwatch();
            var timeOut = TimeSpan.FromSeconds(timeout);
            sw.Start();

            try
            {
                while (sw.Elapsed < timeOut)
                {
                    if (token.IsCancellationRequested) break;
                    clientWs = new ClientWebSocket();

                    clientWs.Options.Cookies = cookies;
                    clientWs.Options.KeepAliveInterval = TimeSpan.FromSeconds(keepAliveIntervalSeconds);
                    if (preConfigureOptions != null)
                    {
                        preConfigureOptions(clientWs.Options);
                    }

                    try
                    {
                        clientWs.ConnectAsync(serverUri, token).ContinueWith(AfterConnect).Wait();
                    }
                    catch (TaskCanceledException)
                    {
                        return false;
                    }
                    catch (OperationCanceledException)
                    {
                        return false;
                    }
                    catch (WebSocketException)
                    {
                        // do nothing, continue
                    }

                    Task.Delay(100).Wait();

                    // Check if connected
                    if (clientWs.State == WebSocketState.Open)
                    {
                        return true;
                    }
                }
            }
            catch (TaskCanceledException)
            {

            }
            catch (OperationCanceledException)
            {

            }

            return false;
        }

        /// <summary>
        /// Start the client and attempt to connect to the server until the timeout is reached.
        /// </summary>
        /// <param name="timeout">Timeout in seconds.</param>
        /// <param name="token">Cancellation token to terminate connection attempts.</param>
        /// <returns>Task returning Boolean indicating if the connection was successful.</returns>
        public async Task<bool> StartWithTimeoutAsync(int timeout = 30, CancellationToken token = default)
        {
            if (timeout < 1) throw new ArgumentException("Timeout must be greater than zero seconds.");

            Stats = new Statistics();
            if (AcceptInvalidCertificates) SetInvalidCertificateAcceptance();

            var sw = new Stopwatch();
            var timeOut = TimeSpan.FromSeconds(timeout);
            sw.Start();

            try
            {
                while (sw.Elapsed < timeOut)
                {
                    if (token.IsCancellationRequested) break;
                    clientWs = new ClientWebSocket();

                    clientWs.Options.Cookies = cookies;
                    clientWs.Options.KeepAliveInterval = TimeSpan.FromSeconds(keepAliveIntervalSeconds);
                    if (preConfigureOptions != null)
                    {
                        preConfigureOptions(clientWs.Options);
                    }

                    try
                    {
                        await clientWs.ConnectAsync(serverUri, token).ContinueWith(AfterConnect);
                    }
                    catch (TaskCanceledException)
                    {
                        return false;
                    }
                    catch (OperationCanceledException)
                    {
                        return false;
                    }
                    catch (WebSocketException)
                    {
                        // do nothing
                    }

                    await Task.Delay(100);

                    // Check if connected
                    if (clientWs.State == WebSocketState.Open)
                    {
                        return true;
                    }
                }
            }
            catch (TaskCanceledException)
            {

            }
            catch (OperationCanceledException)
            {

            }

            return false;
        }

        /// <summary>
        /// Disconnect the client.
        /// </summary>
        public void Stop()
        {
            Stop(WebSocketCloseStatus.NormalClosure, clientWs.CloseStatusDescription);
        }

        /// <summary>
        /// Disconnect the client.
        /// </summary>
        public async Task StopAsync()
        {
            await StopAsync(WebSocketCloseStatus.NormalClosure, clientWs.CloseStatusDescription);
        }

        /// <summary>
        /// Disconnect the client by code and reason.
        /// </summary>
        /// <param name="closeCode">Close code.</param>
        /// <param name="reason">Close by reason.</param>
        public void Stop(WebSocketCloseStatus closeCode, string reason)
        { 
            clientWs.CloseOutputAsync(closeCode, reason, token).Wait();
        } 
        
        /// <summary>
        /// Disconnect the client by code and reason.
        /// </summary>
        /// <param name="closeCode">Close code.</param>
        /// <param name="reason">Close by reason.</param>
        public async Task StopAsync(WebSocketCloseStatus closeCode, string reason)
        { 
            await clientWs.CloseOutputAsync(closeCode, reason, token).ConfigureAwait(false);
        } 

        /// <summary>
        /// Send text data to the server asynchronously.
        /// </summary>
        /// <param name="data">String data.</param>
        /// <param name="msgType">Message type.</param>
        /// <param name="token">Cancellation token allowing for termination of this request.</param>
        /// <returns>Task with Boolean indicating if the message was sent successfully.</returns>
        public async Task<bool> SendAsync(string data, WebSocketMessageType msgType = WebSocketMessageType.Text, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(data)) throw new ArgumentNullException(nameof(data));
            return await MessageWriteAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(data)), msgType, token);
        }

        /// <summary>
        /// Send binary data to the server asynchronously.
        /// </summary>
        /// <param name="data">Byte array containing data.</param>
        /// <param name="msgType">Message type.</param>
        /// <param name="token">Cancellation token allowing for termination of this request.</param>
        /// <returns>Task with Boolean indicating if the message was sent successfully.</returns>
        public async Task<bool> SendAsync(byte[]? data, WebSocketMessageType msgType = WebSocketMessageType.Binary, CancellationToken token = default)
        {
            return await MessageWriteAsync(new ArraySegment<byte>(data), msgType, token);
        }

        /// <summary>
        /// Send binary data to the server asynchronously.
        /// </summary>
        /// <param name="data">ArraySegment containing data.</param>
        /// <param name="msgType">Message type.</param>
        /// <param name="token">Cancellation token allowing for termination of this request.</param>
        /// <returns>Task with Boolean indicating if the message was sent successfully.</returns>
        public async Task<bool> SendAsync(ArraySegment<byte> data, WebSocketMessageType msgType = WebSocketMessageType.Binary, CancellationToken token = default)
        {
            if (data.Array == null || data.Count < 1) throw new ArgumentNullException(nameof(data));
            return await MessageWriteAsync(data, msgType, token);
        }

        /// <summary>
        /// Send text data to the server and wait for a response.
        /// </summary>
        /// <param name="data">String data.</param>
        /// <param name="timeout">Timeout, in seconds.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>String from response.</returns>
        public async Task<string?> SendAndWaitAsync(string data, int timeout = 30, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(data)) throw new ArgumentNullException(nameof(data));
            if (timeout < 1) throw new ArgumentException("Timeout must be greater than zero seconds.", nameof(data));
            string? result = null;

            var receivedEvent = new ManualResetEvent(false);
            await awaitingSyncResponseLock.WaitAsync(this.token);

            await Task.Run(async () =>
            {
                AwaitingSyncResponseEvent += (s, e) =>
                {
                    result = Encoding.UTF8.GetString(e.Data.Array, 0, e.Data.Count);
                    receivedEvent.Set();
                };

                await MessageWriteAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(data)), WebSocketMessageType.Text, token);
                receivedEvent.WaitOne(TimeSpan.FromSeconds(timeout));

                AwaitingSyncResponseEvent = null;
                awaitingSyncResponseLock.Release();
            });

            return result;
        }

        /// <summary>
        /// Send binary data to the server and wait for a response.
        /// </summary>
        /// <param name="data">Byte array containing data.</param>
        /// <param name="timeout">Timeout, in seconds.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Byte array from response.</returns>
        public async Task<ArraySegment<byte>> SendAndWaitAsync(byte[] data, int timeout = 30, CancellationToken token = default)
        {
            return await SendAndWaitAsync(new ArraySegment<byte>(data), timeout, token);
        }

        /// <summary>
        /// Send binary data to the server and wait for a response.
        /// </summary>
        /// <param name="data">ArraySegment containing data.</param>
        /// <param name="timeout">Timeout, in seconds.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Byte array from response.</returns>
        public async Task<ArraySegment<byte>> SendAndWaitAsync(ArraySegment<byte> data, int timeout = 30, CancellationToken token = default)
        {
            if (data.Array == null || data.Count < 1) throw new ArgumentNullException(nameof(data));
            if (timeout < 1) throw new ArgumentException("Timeout must be zero or greater.", nameof(data));
            ArraySegment<byte> result = default;

            var receivedEvent = new ManualResetEvent(false);
            await awaitingSyncResponseLock.WaitAsync(this.token);

            await Task.Run(async () =>
            {
                AwaitingSyncResponseEvent += (s, e) =>
                {
                    result = e.Data;
                    receivedEvent.Set();
                };

                await MessageWriteAsync(data, WebSocketMessageType.Binary, token);
                receivedEvent.WaitOne(TimeSpan.FromSeconds(timeout));

                AwaitingSyncResponseEvent = null;
                awaitingSyncResponseLock.Release();
            });

            return result;
        }

        /// <summary>
        /// Tear down the client and dispose of background workers.
        /// </summary>
        /// <param name="disposing">Disposing.</param>
        private void Dispose(bool disposing)
        {
            if (!disposing) return;
            
            // see https://mcguirev10.com/2019/08/17/how-to-close-websocket-correctly.html  
            if (clientWs is {State: WebSocketState.Open})
            {
                Stop();
                clientWs.Dispose();
            }

            tokenSource.Cancel();

            Logger?.Invoke(Header + "dispose complete");
        }

        private void SetInvalidCertificateAcceptance()
        {
            if (clientWs.State == WebSocketState.Open)
            {
                clientWs.Options.RemoteCertificateValidationCallback += (_, _, _, _) => true;
            }
        }

        private void AfterConnect(Task task)
        {
            if (!task.IsCompleted) return;
            if (clientWs.State == WebSocketState.Open)
            {
                Task.Run(() =>
                {
                    Task.Run(DataReceiver, token);
                    ServerConnected?.Invoke(this, EventArgs.Empty);
                }, token);
            }
        }

        private async Task DataReceiver()
        {
            var buffer = new byte[65536];

            try
            {
                while (true)
                {
                    if (token.IsCancellationRequested) break;
                    var msg = await MessageReadAsync(buffer);

                    if (EnableStatistics)
                    {
                        Stats.IncrementReceivedMessages();
                        Stats.AddReceivedBytes(msg.Data.Count);
                    }

                    if (msg.MessageType == WebSocketMessageType.Close) continue;
                    if (AwaitingSyncResponseEvent != null)
                    {
                        AwaitingSyncResponseEvent?.Invoke(this, msg);
                    }
                    else
                    {
                        MessageReceived?.Invoke(this, msg);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Logger?.Invoke(Header + "data receiver canceled");
            }
            catch (WebSocketException)
            {
                Logger?.Invoke(Header + "websocket disconnected");
            }
            catch (Exception e)
            {
                Logger?.Invoke(Header + "exception: " + Environment.NewLine + e);
            }

            ServerDisconnected?.Invoke(this, EventArgs.Empty);
        }
        private async Task<MessageReceivedEventArgs> MessageReadAsync(byte[] buffer)
        {
            // Do not catch exceptions, let them get caught by the data reader to destroy the connection

            ArraySegment<byte> data = default;
            WebSocketReceiveResult? result = default;

            using (var dataMs = new MemoryStream())
            {
                buffer = new byte[buffer.Length];
                var bufferSegment = new ArraySegment<byte>(buffer);

                if (clientWs.State is WebSocketState.CloseReceived or WebSocketState.Closed)
                {
                    throw new WebSocketException("Websocket close received");
                }

                while (clientWs.State == WebSocketState.Open)
                {
                    result = await clientWs.ReceiveAsync(bufferSegment, token);
                    if (result.Count > 0)
                    {
                        await dataMs.WriteAsync(buffer, 0, result.Count);
                    }

                    if (result.EndOfMessage)
                    {
                        data = new ArraySegment<byte>(dataMs.GetBuffer(), 0, (int)dataMs.Length);
                        break;
                    }
                }
            }

            return new ServerMessageReceivedEventArgs(serverIpPort, data, result.MessageType);
        }

        private async Task<bool> MessageWriteAsync(ArraySegment<byte> data, WebSocketMessageType msgType, CancellationToken token)
        {
            var disconnectDetected = false;
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(this.token, token);
            
            try
            {
                if (clientWs is not {State: WebSocketState.Open})
                {
                    Logger?.Invoke(Header + "not connected");
                    disconnectDetected = true;
                    return false;
                }

                await sendLock.WaitAsync(this.token).ConfigureAwait(false);

                try
                {
                    await clientWs.SendAsync(data, msgType, true, token).ConfigureAwait(false);
                }
                catch
                {
                    // ignored
                }
                finally
                {
                    sendLock.Release();
                }

                if (EnableStatistics)
                {
                    Stats.IncrementSentMessages();
                    Stats.AddSentBytes(data.Count);
                }

                return true;
            }
            catch (TaskCanceledException)
            {
                if (this.token.IsCancellationRequested)
                {
                    Logger?.Invoke(Header + "canceled");
                    disconnectDetected = true;
                }
                else if (token.IsCancellationRequested)
                {
                    Logger?.Invoke(Header + "message send canceled");
                }

                return false;
            }
            catch (OperationCanceledException)
            {
                if (this.token.IsCancellationRequested)
                {
                    Logger?.Invoke(Header + "canceled");
                    disconnectDetected = true;
                }
                else if (token.IsCancellationRequested)
                {
                    Logger?.Invoke(Header + "message send canceled");
                }

                return false;
            }
            catch (WebSocketException)
            {
                Logger?.Invoke(Header + "websocket disconnected");
                disconnectDetected = true;
                return false;
            }
            catch (ObjectDisposedException)
            {
                Logger?.Invoke(Header + "disposed");
                disconnectDetected = true;
                return false;
            }
            catch (SocketException)
            {
                Logger?.Invoke(Header + "socket disconnected");
                disconnectDetected = true;
                return false;
            }
            catch (InvalidOperationException)
            {
                Logger?.Invoke(Header + "disconnected due to invalid operation");
                disconnectDetected = true;
                return false;
            }
            catch (IOException)
            {
                Logger?.Invoke(Header + "IO disconnected");
                disconnectDetected = true;
                return false;
            }
            catch (Exception e)
            {
                Logger?.Invoke(Header + "exception: " + Environment.NewLine + e);
                disconnectDetected = true;
                return false;
            }
            finally
            {
                if (disconnectDetected)
                {
                    Dispose();
                    ServerDisconnected?.Invoke(this, EventArgs.Empty);
                }
            }
        }
    }
}
