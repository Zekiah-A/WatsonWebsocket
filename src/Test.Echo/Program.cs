﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using WatsonWebsocket;

namespace Test.Echo
{
    internal class Program
    {
        private static readonly string Hostname = "localhost";
        private static readonly int Port = 8000;
        private static WatsonWsServer server;
        private static string clientIpPort;
        private static readonly long ServerSendMessageCount = 5000;
        private static readonly int ServerMessageLength = 16;
        private static readonly long ClientSendMessageCount = 5000;
        private static readonly int ClientMessageLength = 16;

        private static readonly Statistics ServerStats = new Statistics();
        private static readonly Statistics ClientStats = new Statistics();

        private static void Main(string[] args)
        {
            var header = "[Server] ";
            using (server = new WatsonWsServer(Hostname, Port, false))
            {
                #region Start-Server
                 
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

                #endregion

                #region Start-Client-and-Send-Messages

                Task.Run(() => ClientTask()); 

                Task.Delay(1000).Wait();

                while (string.IsNullOrEmpty(clientIpPort)) 
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

                #endregion

                #region Wait-for-and-Echo-Client-Messages

                while (!string.IsNullOrEmpty(clientIpPort))
                {
                    Console.WriteLine(header + "waiting for client to finish");
                    Task.Delay(1000).Wait();
                }

                #endregion

                #region Statistics

                Console.WriteLine("");
                Console.WriteLine("");
                Console.WriteLine("Server statistics:");
                Console.WriteLine("  " + ServerStats.ToString());
                Console.WriteLine("");
                Console.WriteLine("Client statistics");
                Console.WriteLine("  " + ClientStats.ToString());
                Console.WriteLine("");

                #endregion

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

            #region Start-Client

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

            #endregion

            #region Wait-for-Messages

            while (ClientStats.MsgRecv < ServerSendMessageCount) 
            {
                Task.Delay(1000).Wait();
                Console.WriteLine(header + "waiting for server messages");
            };

            Console.WriteLine(header + "server messages received");
            #endregion

            #region Send-Messages

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
}
