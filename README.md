### ⚠️ An improved version of watson websocket, that allows for SSL/HTTPS on any OS with ASP.Net kestrel, clean and safe .Net 7 code and proper unique way to identify clients, for real world implementations.

![alt tag](https://github.com/jchristn/watsonwebsocket/blob/master/assets/watson.ico)

# Watson Websocket

[![NuGet Version](https://img.shields.io/nuget/v/WatsonWebsocket.svg?style=flat)](https://www.nuget.org/packages/WatsonWebsocket/) [![NuGet](https://img.shields.io/nuget/dt/WatsonWebsocket.svg)](https://www.nuget.org/packages/WatsonWebsocket) 

WatsonWebsocket is the EASIEST and FASTEST way to build client and server applications that rely on messaging using websockets.  It's.  Really.  Easy.

## Thanks and Appreciation

Many thanks and much appreciation to those that take the time to make this library better!  

@BryanCrotaz @FodderMK @caozero @Danatobob @Data33 @AK5nowman @jjxtra @MartyIX @rajeshdua123 @tersers @MacKey-255 @KRookoo1 @joreg @ilsnk @xbarra

## Test App

A test project for both client (```TestClient```) and server (```TestServer```) are included which will help you understand and exercise the class library.

A test project that spawns a server and client and exchanges messages can be found here: https://github.com/jchristn/watsonwebsockettest

## ⚠️ Supported Operating Systems
***WatsonWebsocketPlus works on all operating systems compatible with Microsoft's ASP.Net Kestrel. Learn more at !(Microsoft ASP.NET Kestrel docs)** 

## ⚠️ SSL
***With WatsonWebsocketPlus, just pass the certificate and key (usually cert.pem & fullchain.pem) paths into the Watson constructor, with ssl set to true, and watson will handle everything for you. It's that easy!***

## Server Example
```csharp
using WatsonWebsocket;

WatsonWsServer server = new WatsonWsServer("[ip]", port, true|false);
server.ClientConnected += ClientConnected;
server.ClientDisconnected += ClientDisconnected;
server.MessageReceived += MessageReceived; 
server.Start();

static void ClientConnected(object sender, ClientConnectedEventArgs args) 
{
    Console.WriteLine("Client connected: " + args.IpPort);
}

static void ClientDisconnected(object sender, ClientDisconnectedEventArgs args) 
{
    Console.WriteLine("Client disconnected: " + args.IpPort);
}

static void MessageReceived(object sender, MessageReceivedEventArgs args) 
{ 
    Console.WriteLine("Message received from " + args.IpPort + ": " + Encoding.UTF8.GetString(args.Data));
}
```

## Client Example
```csharp
using WatsonWebsocket;

WatsonWsClient client = new WatsonWsClient("[server ip]", [server port], true|false);
client.ServerConnected += ServerConnected;
client.ServerDisconnected += ServerDisconnected;
client.MessageReceived += MessageReceived; 
client.Start(); 

static void MessageReceived(object sender, MessageReceivedEventArgs args) 
{
    Console.WriteLine("Message from server: " + Encoding.UTF8.GetString(args.Data));
}

static void ServerConnected(object sender, EventArgs args) 
{
    Console.WriteLine("Server connected");
}

static void ServerDisconnected(object sender, EventArgs args) 
{
    Console.WriteLine("Server disconnected");
}
```

## Client Example using Browser
```csharp
server = new WatsonWsServer("http://localhost:9000/");
server.Start();
```

```js
let socket = new WebSocket("ws://localhost:9000/test/");
socket.onopen = function () { console.log("success"); };
socket.onmessage = function (msg) { console.log(msg.data); };
socket.onclose = function () { console.log("closed"); };
// wait a moment
socket.send("Hello, world!");
```

## ⚠️ Notes:
 - This is still WIP, there are **MANY** breaking changes compared to WatsonWebsocket, to the point of most code being fully incompatible. Please tell me if any essential features have been removed in the changes made.
 - On some operating systems, certain ports may be restricted administrator/root, WatsonWebsocketPlus will have to be run with these privaleges in order to make use of these ports.
 - When using LetsEncrypt/certbot certificates saved in system directories, admin privaleges may be required in order for WatsonWebsocketPlus to read the files
