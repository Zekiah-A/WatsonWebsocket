using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using WatsonWebsocket;

namespace Test.Integrity;

internal static class Program
{
    private const int NumClients = 5;
    private const int MessagesPerClient = 100;
    private const int MsgLength = 4096;
    private static int sendDelay = 100;
    private static byte[]? msgData;

    private const string? Hostname = "localhost";
    private const int Port = 8000;
    private static WatsonWsServer? server;
    private static bool serverReady;

    private static Statistics serverStats = new();
    private static readonly List<Statistics> ClientStats = new();

    private static void Main(string[] args)
    {
        msgData = Encoding.UTF8.GetBytes(RandomString(MsgLength));
        sendDelay = NumClients * 20;

        using (server = new WatsonWsServer(Hostname, Port, false))
        {
            #region Start-Server

            serverStats = new Statistics();

            server.ClientConnected += (s, e) =>
            {
                Console.WriteLine("Client connected: " + e.Client.IpPort);
                    
            };

            server.ClientDisconnected += (s, e) =>
            { 
                Console.WriteLine("*** Client disconnected: " + e.Client.IpPort);
            };

            server.MessageReceived += (s, e) =>
            {
                serverStats.AddRecv(e.Data.Count);
            };

            // server.Logger = Logger;
            server.Start();

            #endregion

            #region Start-and-Wait-for-Clients

            for (var i = 0; i < NumClients; i++)
            {
                Console.WriteLine("Starting client " + (i + 1) + "...");
                Task.Run(() => ClientTask());
                Task.Delay(250).Wait();
            }

            while (true)
            {
                Task.Delay(1000).Wait();
                var connected = 0;
                connected = server.Clients.Count;
                if (connected == NumClients) break;
                Console.WriteLine(connected + " of " + NumClients + " connected, waiting");
            }

            Console.WriteLine("All clients connected!");
            serverReady = true;

            #endregion

            #region Send-Messages-to-Clients

            for (var i = 0; i < MessagesPerClient; i++)
            {
                for (var j = 0; j < NumClients; j++)
                {
                    server.SendAsync(server.Clients[i], msgData).Wait();
                    serverStats.AddSent(msgData.Length);
                }
            }

            #endregion

            #region Wait-for-Clients

            while (true)
            {
                Task.Delay(5000).Wait();
                var remaining = 0;
                remaining = server.Clients.Count;
                if (remaining < 1) break;
                Console.WriteLine(DateTime.Now.ToUniversalTime().ToString("HH:mm:ss.ffffff") + " waiting for " + remaining + " clients: ");
                foreach (var curr in server.Clients) Console.WriteLine("| " + curr.IpPort);
            }

            #endregion

            #region Statistics

            Console.WriteLine("");
            Console.WriteLine("");
            Console.WriteLine("Server statistics:");
            Console.WriteLine("  " + serverStats);
            Console.WriteLine("");
            Console.WriteLine("Client statistics");
            foreach (var stats in ClientStats) Console.WriteLine("  " + stats.ToString());
            Console.WriteLine("");

            #endregion
        }
    }

    private static void Logger(string msg)
    {
        Console.WriteLine(msg);
    }

    private static void ClientTask()
    {
        var stats = new Statistics();

        using var client = new WatsonWsClient(Hostname, Port, false);

        #region Start-Client

        client.ServerConnected += (s, e) =>
        {
            Console.WriteLine("Client detected connection to " + Hostname + ":" + Port);
        };

        client.ServerDisconnected += (s, e) =>
        {
            Console.WriteLine("Client disconnected from " + Hostname + ":" + Port);
        };

        client.MessageReceived += (s, e) =>
        {
            stats.AddRecv(e.Data.Count);
        };

        // client.Logger = Logger;
        client.Start();

        #endregion

        #region Wait-for-Server-Ready

        while (!serverReady)
        {
            Console.WriteLine("Client waiting for server...");
            Task.Delay(2500).Wait();
        }

        Console.WriteLine("Client detected server ready!");

        #endregion

        #region Send-Messages-to-Server

        for (var i = 0; i < MessagesPerClient; i++)
        {
            Task.Delay(sendDelay).Wait();
            client.SendAsync(msgData).Wait();
            stats.AddSent(msgData.Length);
        }

        #endregion

        #region Wait-for-Server-Messages

        while (stats.MsgRecv < MessagesPerClient)
        {
            Task.Delay(1000).Wait();
        }

        Console.WriteLine("Client exiting: " + stats.ToString());
        ClientStats.Add(stats);

        #endregion
    }

    private static string RandomString(int numChar)
    {
        var ret = "";
        if (numChar < 1) return null;
        var valid = 0;
        var random = new Random((int)DateTime.Now.Ticks);
        var num = 0;

        for (var i = 0; i < numChar; i++)
        {
            num = 0;
            valid = 0;
            while (valid == 0)
            {
                num = random.Next(126);
                if ((num > 47 && num < 58) ||
                    (num > 64 && num < 91) ||
                    (num > 96 && num < 123))
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

        if (yesDefault) Console.Write(" [Y/n]? ");
        else Console.Write(" [y/N]? ");

        var userInput = Console.ReadLine();

        if (string.IsNullOrEmpty(userInput))
        {
            if (yesDefault) return true;
            return false;
        }

        userInput = userInput.ToLower();

        if (yesDefault)
        {
            if (
                string.Compare(userInput, "n") == 0
                || string.Compare(userInput, "no") == 0
            )
            {
                return false;
            }

            return true;
        }
        else
        {
            if (
                string.Compare(userInput, "y") == 0
                || string.Compare(userInput, "yes") == 0
            )
            {
                return true;
            }

            return false;
        }
    }

    private static string InputString(string question, string defaultAnswer, bool allowNull)
    {
        while (true)
        {
            Console.Write(question);

            if (!string.IsNullOrEmpty(defaultAnswer))
            {
                Console.Write(" [" + defaultAnswer + "]");
            }

            Console.Write(" ");

            var userInput = Console.ReadLine();

            if (string.IsNullOrEmpty(userInput))
            {
                if (!string.IsNullOrEmpty(defaultAnswer)) return defaultAnswer;
                if (allowNull) return null;
                else continue;
            }

            return userInput;
        }
    }

    private static int InputInteger(string question, int defaultAnswer, bool positiveOnly, bool allowZero)
    {
        while (true)
        {
            Console.Write(question);
            Console.Write(" [" + defaultAnswer + "] ");

            var userInput = Console.ReadLine();

            if (string.IsNullOrEmpty(userInput))
            {
                return defaultAnswer;
            }

            var ret = 0;
            if (!int.TryParse(userInput, out ret))
            {
                Console.WriteLine("Please enter a valid integer.");
                continue;
            }

            if (ret == 0)
            {
                if (allowZero)
                {
                    return 0;
                }
            }

            if (ret < 0)
            {
                if (positiveOnly)
                {
                    Console.WriteLine("Please enter a value greater than zero.");
                    continue;
                }
            }

            return ret;
        }
    }
}