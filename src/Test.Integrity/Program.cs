﻿using System;
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

        using (server = new WatsonWsServer(Port, Hostname))
        {
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
            
            server.Start();

            for (var i = 0; i < NumClients; i++)
            {
                Console.WriteLine("Starting client " + (i + 1) + "...");
                Task.Run(ClientTask);
                Task.Delay(250).Wait();
            }

            while (true)
            {
                Task.Delay(1000).Wait();
                var connected = server.Clients.Count;
                if (connected == NumClients) break;
                Console.WriteLine(connected + " of " + NumClients + " connected, waiting");
            }

            Console.WriteLine("All clients connected!");
            serverReady = true;

            for (var i = 0; i < MessagesPerClient; i++)
            {
                for (var j = 0; j < NumClients; j++)
                {
                    server.SendAsync(server.Clients[i], msgData).Wait();
                    serverStats.AddSent(msgData.Length);
                }
            }

            while (true)
            {
                Task.Delay(5000).Wait();
                var remaining = 0;
                remaining = server.Clients.Count;
                if (remaining < 1) break;
                Console.WriteLine(DateTime.Now.ToUniversalTime().ToString("HH:mm:ss.ffffff") + " waiting for " + remaining + " clients: ");
                foreach (var curr in server.Clients) Console.WriteLine("| " + curr.IpPort);
            }

            Console.WriteLine("");
            Console.WriteLine("");
            Console.WriteLine("Server statistics:");
            Console.WriteLine("  " + serverStats);
            Console.WriteLine("");
            Console.WriteLine("Client statistics");
            foreach (var stats in ClientStats) Console.WriteLine("  " + stats);
            Console.WriteLine("");
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

        while (!serverReady)
        {
            Console.WriteLine("Client waiting for server...");
            Task.Delay(2500).Wait();
        }

        Console.WriteLine("Client detected server ready!");

        for (var i = 0; i < MessagesPerClient; i++)
        {
            Task.Delay(sendDelay).Wait();
            client.SendAsync(msgData).Wait();
            if (msgData != null) stats.AddSent(msgData.Length);
        }

        while (stats.MsgRecv < MessagesPerClient)
        {
            Task.Delay(1000).Wait();
        }

        Console.WriteLine("Client exiting: " + stats);
        ClientStats.Add(stats);
    }

    private static string RandomString(int numChar)
    {
        var ret = "";
        if (numChar < 1) return null;
        var random = new Random((int)DateTime.Now.Ticks);

        for (var i = 0; i < numChar; i++)
        {
            var num = 0;
            var valid = 0;
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

        if (string.IsNullOrEmpty(userInput))
        {
            return yesDefault;
        }

        userInput = userInput.ToLower();

        if (yesDefault)
        {
            return string.CompareOrdinal(userInput, "n") != 0 
                   && string.CompareOrdinal(userInput, "no") != 0;
        }
        else
        {
            return string.CompareOrdinal(userInput, "y") == 0
                   || string.CompareOrdinal(userInput, "yes") == 0;
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

            if (!string.IsNullOrEmpty(userInput)) return userInput;
            if (!string.IsNullOrEmpty(defaultAnswer)) return defaultAnswer;
            if (allowNull) return null;
            else continue;

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