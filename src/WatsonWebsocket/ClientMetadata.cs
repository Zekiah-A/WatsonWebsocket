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
    public string? IpPort => _Ip + ":" + _Port;

    private readonly string _Ip;
    private readonly int _Port;
    private readonly HttpListenerContext _HttpContext;
    private WebSocketContext _WsContext;
    internal WebSocket Ws;
    internal readonly CancellationTokenSource TokenSource;
    internal readonly SemaphoreSlim SendLock = new(1);
         
    internal ClientMetadata(HttpListenerContext httpContext, WebSocket ws, WebSocketContext wsContext, CancellationTokenSource tokenSource)
    {
        _HttpContext = httpContext ?? throw new ArgumentNullException(nameof(httpContext));
        Ws = ws ?? throw new ArgumentNullException(nameof(ws));
        _WsContext = wsContext ?? throw new ArgumentNullException(nameof(wsContext));
        TokenSource = tokenSource ?? throw new ArgumentNullException(nameof(tokenSource)); 
        _Ip = _HttpContext.Request.RemoteEndPoint.Address.ToString();
        _Port = _HttpContext.Request.RemoteEndPoint.Port;
    } 
}