using System;
using System.Net;
using System.Net.WebSockets;
using System.Threading;

namespace WatsonWebsocket;

/// <summary>
/// The instance of a client connected to a server.
/// </summary>
public class ClientMetadata
{ 
    public string? IpPort => ip + ":" + port;

    private readonly string ip;
    private readonly int port;
    private readonly HttpListenerContext httpContext;
    private WebSocketContext wsContext;
    internal WebSocket Ws;
    internal readonly CancellationTokenSource TokenSource;
    internal readonly SemaphoreSlim SendLock = new(1);
         
    internal ClientMetadata(HttpListenerContext httpContext, WebSocket ws, WebSocketContext wsContext, CancellationTokenSource tokenSource)
    {
        this.httpContext = httpContext ?? throw new ArgumentNullException(nameof(httpContext));
        Ws = ws ?? throw new ArgumentNullException(nameof(ws));
        this.wsContext = wsContext ?? throw new ArgumentNullException(nameof(wsContext));
        TokenSource = tokenSource ?? throw new ArgumentNullException(nameof(tokenSource)); 
        ip = this.httpContext.Request.RemoteEndPoint.Address.ToString();
        port = this.httpContext.Request.RemoteEndPoint.Port;
    } 
}