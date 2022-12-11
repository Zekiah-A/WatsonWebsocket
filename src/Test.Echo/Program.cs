using System;
using System.Text;
using System.Threading.Tasks;
using WatsonWebsocket;
using static System.String;

namespace Test.Echo;

internal static class Program
{
    private const string? Hostname = "localhost";
    private const int Port = 8000;
    private static WatsonWsServer? server;
    private static string? clientIpPort;
    private const long ServerSendMessageCount = 5000;
    private const int ServerMessageLength = 16;
    private const long ClientSendMessageCount = 5000;
    private const int ClientMessageLength = 16;

    private static readonly Statistics ServerStats = new();
    private static readonly Statistics ClientStats = new();

    private static void Main(string[] args)
    {
        const string header = "[Server] ";
        using (server = new WatsonWsServer(Port, Hostname))
        {
            server.ClientConnected += (s, e) =>
            {
                clientIpPort = e.Client.IpPort;
                Console.WriteLine(header + "client connected: " + e.Client.IpPort);
            };

            server.ClientDisconnected += (s, e) =>
            {
                clientIpPort = null;
                Console.WriteLine(header + "client disconnected: " + e.Client.IpPort);
            };

            server.MessageReceived += async (s, e) =>
            {
                // echo it back
                ServerStats.AddRecv(e.Data.Count);
                await server.SendAsync(e.Client, e.Data);
                ServerStats.AddSent(e.Data.Count);
            };

            server.Logger = Logger;
            server.Start();
            Console.WriteLine(header + "started");

            Task.Run(ClientTask); 

            Task.Delay(1000).Wait();

            while (IsNullOrEmpty(clientIpPort)) 
            {
                Task.Delay(1000).Wait();
                Console.WriteLine(header + "waiting for client connection");
            };

            Console.WriteLine(header + "detected client " + clientIpPort + ", sending messages");
                 
            for (var i = 0; i < ServerSendMessageCount; i++)
            {
                var msgData = Encoding.UTF8.GetBytes(RandomString(ServerMessageLength));
                server.SendAsync(server.GetClientFromIpPort(clientIpPort), msgData).Wait();
                ServerStats.AddSent(msgData.Length);
            }

            Console.WriteLine(header + "messages sent");

            while (!IsNullOrEmpty(clientIpPort))
            {
                Console.WriteLine(header + "waiting for client to finish");
                Task.Delay(1000).Wait();
            }

            Console.WriteLine("");
            Console.WriteLine("");
            Console.WriteLine("Server statistics:");
            Console.WriteLine("  " + ServerStats);
            Console.WriteLine("");
            Console.WriteLine("Client statistics");
            Console.WriteLine("  " + ClientStats);
            Console.WriteLine("");

            Console.WriteLine("Press ENTER to exit");
            Console.ReadLine();
        }
    }

    private static void Logger(string msg)
    {
        Console.WriteLine(msg);
    }

    private static async void ClientTask()
    {
        var header = "[Client] ";

        using var client = new WatsonWsClient(Hostname, Port, false);

        client.ServerConnected += (s, e) =>
        { 
            Console.WriteLine(header + "connected to " + Hostname + ":" + Port);
        };

        client.ServerDisconnected += (s, e) =>
        {
            Console.WriteLine(header + "disconnected from " + Hostname + ":" + Port);
        };

        client.MessageReceived += (s, e) =>
        {
            ClientStats.AddRecv(e.Data.Count);
        };

        client.Logger = Logger;
        client.Start();
        Console.WriteLine(header + "started");

        while (ClientStats.MsgRecv < ServerSendMessageCount) 
        {
            Task.Delay(1000).Wait();
            Console.WriteLine(header + "waiting for server messages");
        };

        Console.WriteLine(header + "server messages received");

        Console.WriteLine(header + "sending messages to server");

        for (var i = 0; i < ClientSendMessageCount; i++)
        {
            var msgData = Encoding.UTF8.GetBytes(RandomString(ClientMessageLength));
            await client.SendAsync(msgData);
            ClientStats.AddSent(msgData.Length);
        }

        while (ClientStats.MsgRecv < ClientSendMessageCount + ServerSendMessageCount)
        {
            Console.WriteLine(header + "waiting for server echo messages");
            Task.Delay(1000).Wait();
        }

        Console.WriteLine(header + "finished");
        clientIpPort = null;
    }

    private static string RandomString(int numChar)
    {
        var ret = "";
        if (numChar < 1) return null;
        int valid;
        int num;
        var random = new Random((int)DateTime.Now.Ticks);

        for (var i = 0; i < numChar; i++)
        {
            num = 0;
            valid = 0;
            while (valid == 0)
            {
                num = random.Next(126);
                if (num is > 47 and < 58 or > 64 and < 91 or > 96 and < 123)
                {
                    valid = 1;
                }
            }
            ret += (char)num;
        }

        return ret;
    }

    private static bool InputBoolean(string question, bool yesDefault)
    {
        Console.Write(question);

        Console.Write(yesDefault ? " [Y/n]? " : " [y/N]? ");

        var userInput = Console.ReadLine();

        if (IsNullOrEmpty(userInput))
        {
            return yesDefault;
        }

        userInput = userInput.ToLower();

        if (yesDefault)
        {
            return CompareOrdinal(userInput, "n") != 0 && CompareOrdinal(userInput, "no") != 0;
        }
        
        return Compare(userInput, "y") == 0 || Compare(userInput, "yes") == 0;
    }

    private static string? InputString(string question, string? defaultAnswer, bool allowNull)
    {
        while (true)
        {
            Console.Write(question);

            if (!IsNullOrEmpty(defaultAnswer))
            {
                Console.Write(" [" + defaultAnswer + "]");
            }

            Console.Write(" ");

            var userInput = Console.ReadLine();

            if (!IsNullOrEmpty(userInput)) return userInput;
            if (!IsNullOrEmpty(defaultAnswer)) return defaultAnswer;
            if (allowNull) return null;
        }
    }

    private static int InputInteger(string question, int defaultAnswer, bool positiveOnly, bool allowZero)
    {
        while (true)
        {
            Console.Write(question);
            Console.Write(" [" + defaultAnswer + "] ");

            var userInput = Console.ReadLine();

            if (IsNullOrEmpty(userInput))
            {
                return defaultAnswer;
            }

            if (!int.TryParse(userInput, out var ret))
            {
                Console.WriteLine("Please enter a valid integer.");
                continue;
            }

            switch (ret)
            {
                case 0 when allowZero:
                    return 0;
                case < 0 when positiveOnly:
                    Console.WriteLine("Please enter a value greater than zero.");
                    continue;
                default:
                    return ret;
            }
        }
    }
}