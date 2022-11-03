using System;
using System.Threading.Tasks;
using WatsonWebsocket;

namespace Test.Dispose;

internal static class Program
{
    private static WatsonWsServer? server;

    private static void Main(string[] args)
    {
        // test1
        server = new WatsonWsServer();
        server.ClientConnected += (s, a) => { /* Console.WriteLine("Client " + a.IpPort + " connected"); */ };
        server.ClientDisconnected += (s, a) => { /* Console.WriteLine("Client " + a.IpPort + " disconnected"); */ };
        server.MessageReceived += (s, a) => { /* Console.WriteLine(Encoding.UTF8.GetString(a.Data)); */ }; 
        server.Start();
        Console.WriteLine("Test 1 with server started: " + ClientTask());

        // test2
        Task.Delay(1000).Wait();
        server.Stop();
        Console.WriteLine("Test 2 with server stopped: " + ClientTask());

        // test3
        Task.Delay(1000).Wait();
        server.Start();
        Console.WriteLine("Test 3 with server restarted: " + ClientTask());

        // test4
        Task.Delay(1000).Wait();
        server.Dispose();
        Console.WriteLine("Test 4 with server disposed: " + ClientTask());

        // test5
        Task.Delay(1000).Wait();
        server = new WatsonWsServer();
        server.ClientConnected += (s, a) => { /* Console.WriteLine("Client " + a.IpPort + " connected"); */ };
        server.ClientDisconnected += (s, a) => { /* Console.WriteLine("Client " + a.IpPort + " disconnected"); */ };
        server.MessageReceived += (s, a) => { /* Console.WriteLine(Encoding.UTF8.GetString(a.Data)); */ }; 
        server.Start();
        Console.WriteLine("Test 5 with server started: " + ClientTask()); 
    }

    private static bool ClientTask()
    { 
        try
        {
            var client = new WatsonWsClient("localhost", 9000, false);
            client.Start();
            Task.Delay(1000).Wait();
            return client.SendAsync("Hello").Result;
        }
        catch (Exception e)
        {
            Console.WriteLine(e.ToString());
            return false;
        }
    }
}