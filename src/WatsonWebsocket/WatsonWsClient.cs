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
        #region Public-Members

        /// <summary>
        /// Indicate whether or not invalid or otherwise unverifiable certificates should be accepted.  Default is true.
        /// </summary>
        public bool AcceptInvalidCertificates { get; set; }
        /// <summary>
        /// Indicates whether or not the client is connected to the server.
        /// </summary>
        public bool Connected => ClientWs.State == WebSocketState.Open;

        /// <summary>
        /// Enable or disable statistics.
        /// </summary>
        public bool EnableStatistics { get; set; } = true;

        /// <summary>
        /// Set KeepAlive to Connection Options.
        /// </summary>
        public int KeepAliveInterval
        {
            get => KeepAliveIntervalSeconds;
            set
            {
                if (value < 1) throw new ArgumentException("ConnectTimeoutSeconds must be greater than zero.");
                KeepAliveIntervalSeconds = value;
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

        #endregion

        #region Private-Members

        private const string Header = "[WatsonWsClient] ";
        private readonly Uri ServerUri;
        private readonly string ServerIp;
        private readonly int ServerPort;
        private readonly string ServerIpPort;
        private ClientWebSocket ClientWs;
        private int KeepAliveIntervalSeconds = 30;
        private readonly CookieContainer Cookies = new();
        private Action<ClientWebSocketOptions>? PreConfigureOptions;

        private event EventHandler<MessageReceivedEventArgs>? AwaitingSyncResponseEvent;

        private readonly SemaphoreSlim SendLock = new(1);
        private readonly SemaphoreSlim AwaitingSyncResponseLock = new(1);

        private readonly CancellationTokenSource TokenSource = new();
        private readonly CancellationToken Token;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes the Watson websocket client.
        /// Be sure to call 'Start()' to start the client and connect to the server.
        /// </summary>
        /// <param name="serverIp">IP address of the server.</param>
        /// <param name="serverPort">TCP port of the server.</param>
        /// <param name="ssl">Enable or disable SSL.</param>
        public WatsonWsClient(string serverIp, int serverPort, bool ssl)
        {
            string? url;
            if (string.IsNullOrEmpty(serverIp)) throw new ArgumentNullException(nameof(serverIp));
            if (serverPort < 1) throw new ArgumentOutOfRangeException(nameof(serverPort));

            ServerIp = serverIp;
            ServerPort = serverPort;
            ServerIpPort = serverIp + ":" + serverPort;

            if (ssl) url = "wss://" + ServerIp + ":" + ServerPort;
            else url = "ws://" + ServerIp + ":" + ServerPort;
            ServerUri = new Uri(url);
            Token = TokenSource.Token;

            ClientWs = new ClientWebSocket();
        }

        /// <summary>
        /// Initializes the Watson websocket client.
        /// Be sure to call 'Start()' to start the client and connect to the server.
        /// </summary>
        /// <param name="uri">The URI of the server endpoint.</param> 
        public WatsonWsClient(Uri uri)
        {
            ServerUri = uri;
            var serverIp = uri.Host;
            var serverPort = uri.Port;
            ServerIpPort = uri.Host + ":" + uri.Port;
            Token = TokenSource.Token;

            ClientWs = new ClientWebSocket();
        }

        #endregion

        #region Public-Methods

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
                PreConfigureOptions = options;
            }

            return this;
        }

        /// <summary>
        /// Add a cookie prior to connecting to the server.
        /// </summary>
        public void AddCookie(Cookie cookie) => Cookies.Add(cookie);

        /// <summary>
        /// Start the client and connect to the server.
        /// </summary>
        public void Start()
        {
            Stats = new Statistics();
            if (AcceptInvalidCertificates) SetInvalidCertificateAcceptance();

            ClientWs.Options.Cookies = Cookies;
            ClientWs.Options.KeepAliveInterval = TimeSpan.FromSeconds(KeepAliveIntervalSeconds);

            PreConfigureOptions?.Invoke(ClientWs.Options);

            ClientWs.ConnectAsync(ServerUri, Token).ContinueWith(AfterConnect).Wait();
        }

        /// <summary>
        /// Start the client and connect to the server.
        /// </summary>
        /// <returns>Task.</returns>
        public Task StartAsync()
        {
            Stats = new Statistics();
            if (AcceptInvalidCertificates) SetInvalidCertificateAcceptance();

            ClientWs.Options.Cookies = Cookies;
            ClientWs.Options.KeepAliveInterval = TimeSpan.FromSeconds(KeepAliveIntervalSeconds);

            if (PreConfigureOptions != null)
            {
                PreConfigureOptions(ClientWs.Options);
            }

            // Connect
            return ClientWs.ConnectAsync(ServerUri, Token).ContinueWith(AfterConnect);
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
                    ClientWs = new ClientWebSocket();

                    ClientWs.Options.Cookies = Cookies;
                    ClientWs.Options.KeepAliveInterval = TimeSpan.FromSeconds(KeepAliveIntervalSeconds);
                    if (PreConfigureOptions != null)
                    {
                        PreConfigureOptions(ClientWs.Options);
                    }

                    try
                    {
                        ClientWs.ConnectAsync(ServerUri, token).ContinueWith(AfterConnect).Wait();
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
                    if (ClientWs.State == WebSocketState.Open)
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
                    ClientWs = new ClientWebSocket();

                    ClientWs.Options.Cookies = Cookies;
                    ClientWs.Options.KeepAliveInterval = TimeSpan.FromSeconds(KeepAliveIntervalSeconds);
                    if (PreConfigureOptions != null)
                    {
                        PreConfigureOptions(ClientWs.Options);
                    }

                    try
                    {
                        await ClientWs.ConnectAsync(ServerUri, token).ContinueWith(AfterConnect);
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
                    if (ClientWs.State == WebSocketState.Open)
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
            Stop(WebSocketCloseStatus.NormalClosure, ClientWs.CloseStatusDescription);
        }

        /// <summary>
        /// Disconnect the client.
        /// </summary>
        public async Task StopAsync()
        {
            await StopAsync(WebSocketCloseStatus.NormalClosure, ClientWs.CloseStatusDescription);
        }

        /// <summary>
        /// Disconnect the client by code and reason.
        /// </summary>
        /// <param name="closeCode">Close code.</param>
        /// <param name="reason">Close by reason.</param>
        public void Stop(WebSocketCloseStatus closeCode, string reason)
        { 
            ClientWs.CloseOutputAsync(closeCode, reason, Token).Wait();
        } 
        
        /// <summary>
        /// Disconnect the client by code and reason.
        /// </summary>
        /// <param name="closeCode">Close code.</param>
        /// <param name="reason">Close by reason.</param>
        public async Task StopAsync(WebSocketCloseStatus closeCode, string reason)
        { 
            await ClientWs.CloseOutputAsync(closeCode, reason, Token).ConfigureAwait(false);
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
        public async Task<bool> SendAsync(byte[] data, WebSocketMessageType msgType = WebSocketMessageType.Binary, CancellationToken token = default)
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
            await AwaitingSyncResponseLock.WaitAsync(Token);

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
                AwaitingSyncResponseLock.Release();
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
            await AwaitingSyncResponseLock.WaitAsync(Token);

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
                AwaitingSyncResponseLock.Release();
            });

            return result;
        }

        #endregion

        #region Private-Methods

        /// <summary>
        /// Tear down the client and dispose of background workers.
        /// </summary>
        /// <param name="disposing">Disposing.</param>
        private void Dispose(bool disposing)
        {
            if (!disposing) return;
            
            // see https://mcguirev10.com/2019/08/17/how-to-close-websocket-correctly.html  
            if (ClientWs is {State: WebSocketState.Open})
            {
                Stop();
                ClientWs.Dispose();
            }

            TokenSource.Cancel();

            Logger?.Invoke(Header + "dispose complete");
        }

        private void SetInvalidCertificateAcceptance()
        {
            if (ClientWs.State == WebSocketState.Open)
            {
                ClientWs.Options.RemoteCertificateValidationCallback += (_, _, _, _) => true;
            }
        }

        private void AfterConnect(Task task)
        {
            if (!task.IsCompleted) return;
            if (ClientWs.State == WebSocketState.Open)
            {
                Task.Run(() =>
                {
                    Task.Run(DataReceiver, Token);
                    ServerConnected?.Invoke(this, EventArgs.Empty);
                }, Token);
            }
        }

        private async Task DataReceiver()
        {
            var buffer = new byte[65536];

            try
            {
                while (true)
                {
                    if (Token.IsCancellationRequested) break;
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

                if (ClientWs.State is WebSocketState.CloseReceived or WebSocketState.Closed)
                {
                    throw new WebSocketException("Websocket close received");
                }

                while (ClientWs.State == WebSocketState.Open)
                {
                    result = await ClientWs.ReceiveAsync(bufferSegment, Token);
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

            return new ServerMessageReceivedEventArgs(ServerIpPort, data, result.MessageType);
        }

        private async Task<bool> MessageWriteAsync(ArraySegment<byte> data, WebSocketMessageType msgType, CancellationToken token)
        {
            var disconnectDetected = false;
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(Token, token);
            
            try
            {
                if (ClientWs is not {State: WebSocketState.Open})
                {
                    Logger?.Invoke(Header + "not connected");
                    disconnectDetected = true;
                    return false;
                }

                await SendLock.WaitAsync(Token).ConfigureAwait(false);

                try
                {
                    await ClientWs.SendAsync(data, msgType, true, token).ConfigureAwait(false);
                }
                catch
                {
                    // ignored
                }
                finally
                {
                    SendLock.Release();
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
                if (Token.IsCancellationRequested)
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
                if (Token.IsCancellationRequested)
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

        #endregion
    }
}
