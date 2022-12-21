using System;
using System.Net;
using System.Net.WebSockets;
using System.Threading;
using Microsoft.AspNetCore.Http;

namespace WatsonWebsocket;

/// <summary>
/// The instance of a client connected to a server.
/// </summary>
public class ClientMetadata
{ 
    public string IpPort => ip + ":" + port; 
    public HttpContext HttpContext { get; }
    internal readonly WebSocket Ws;
    internal readonly CancellationTokenSource TokenSource;
    internal readonly SemaphoreSlim SendLock = new(1);
    
    private readonly string ip;
    private readonly int port;

    internal ClientMetadata(HttpContext httpContext, WebSocket ws, CancellationTokenSource tokenSource)
    {
        HttpContext = httpContext;
        Ws = ws;
        TokenSource = tokenSource; 
        ip = HttpContext.Connection.RemoteIpAddress!.ToString();
        port = HttpContext.Connection.RemotePort;
    } 
}