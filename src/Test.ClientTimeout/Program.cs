using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WatsonWebsocket;

namespace Test.ClientTimeout
{
    internal class Program
    {
        private static string? serverIp = "";
        private static int serverPort;
        private static bool ssl;
        private static readonly bool AcceptInvalidCertificates = true;
        private static WatsonWsClient? client;

        private static void Main(string[] args)
        {
            serverIp = InputString("Server IP:", "localhost", true);
            serverPort = InputInteger("Server port:", 9000, true, true);
            ssl = InputBoolean("Use SSL:", false);

            InitializeClient();

            var runForever = true;
            while (runForever)
            {
                Console.Write("Command [? for help]: ");
                var userInput = Console.ReadLine();
                if (string.IsNullOrEmpty(userInput)) continue;

                switch (userInput)
                {
                    case "?":
                        Console.WriteLine("Available commands:");
                        Console.WriteLine("  ?            help (this menu)");
                        Console.WriteLine("  q            quit");
                        Console.WriteLine("  cls          clear screen");
                        Console.WriteLine("  send text    send text to the server");
                        Console.WriteLine("  send bytes   send binary data to the server");
                        Console.WriteLine("  sync text    send text to the server and await response");
                        Console.WriteLine("  sync bytes   send binary data to the server and await response");
                        Console.WriteLine("  stats        display client statistics");
                        Console.WriteLine("  status       show if client connected");
                        Console.WriteLine("  dispose      dispose of the connection");
                        Console.WriteLine("  connect      connect to the server if not connected");
                        Console.WriteLine("  reconnect    disconnect if connected, then reconnect");
                        Console.WriteLine("  close        close the connection");
                        break;

                    case "q":
                        runForever = false;
                        break;

                    case "cls":
                        Console.Clear();
                        break;

                    case "send text":
                        Console.Write("Data: ");
                        userInput = Console.ReadLine();
                        if (string.IsNullOrEmpty(userInput)) break;
                        if (!client.SendAsync(userInput).Result) Console.WriteLine("Failed");
                        else Console.WriteLine("Success");
                        break;

                    case "send bytes":
                        Console.Write("Data: ");
                        userInput = Console.ReadLine();
                        if (string.IsNullOrEmpty(userInput)) break;
                        if (!client.SendAsync(Encoding.UTF8.GetBytes(userInput)).Result) Console.WriteLine("Failed");
                        break;

                    case "sync text":
                        Console.Write("Data: ");
                        userInput = Console.ReadLine();
                        if (string.IsNullOrEmpty(userInput)) break;
                        var resultStr = client.SendAndWaitAsync(userInput).Result;
                        if (!string.IsNullOrEmpty(resultStr))
                        {
                            Console.WriteLine("Response: " + resultStr);
                        }
                        else
                        {
                            Console.WriteLine("(null)");
                        }
                        break;

                    case "sync bytes":
                        Console.Write("Data: ");
                        userInput = Console.ReadLine();
                        if (string.IsNullOrEmpty(userInput)) break;
                        var resultBytes = client.SendAndWaitAsync(Encoding.UTF8.GetBytes(userInput)).Result;
                        if (resultBytes != null && resultBytes.Count > 0)
                        {
                            Console.WriteLine("Response: " + Encoding.UTF8.GetString(resultBytes.Array, 0, resultBytes.Count));
                        }
                        else
                        {
                            Console.WriteLine("(null)");
                        }
                        break;

                    case "stats":
                        Console.WriteLine(client.Stats.ToString());
                        break;

                    case "status":
                        if (client == null) Console.WriteLine("Connected: False (null)");
                        else Console.WriteLine("Connected: " + client.Connected);
                        break;

                    case "dispose":
                        client.Dispose();
                        break;

                    case "connect":
                        if (client != null && client.Connected)
                        {
                            Console.WriteLine("Already connected");
                        }
                        else
                        {
                            InitializeClient();
                        }
                        break;

                    case "reconnect":
                        InitializeClient();
                        break;

                    case "close":
                        client.Stop();
                        break;

                    default:
                        break;
                }
            }
        }

        private static void InitializeClient()
        {
            client.Dispose();
            
            // URI-based constructor
            client = ssl ? 
                new WatsonWsClient(new Uri("wss://" + serverIp + ":" + serverPort)) 
                : new WatsonWsClient(new Uri("ws://" + serverIp + ":" + serverPort));

            client.AcceptInvalidCertificates = AcceptInvalidCertificates;
            client.ServerConnected += ServerConnected;
            client.ServerDisconnected += ServerDisconnected;
            client.MessageReceived += MessageReceived;
            client.Logger = Logger;
            client.AddCookie(new System.Net.Cookie("foo", "bar", "/", "localhost"));
            
            while (!client.Connected)
            {
                try
                {
                    Console.WriteLine("Attempting connection...");
                    client.StartWithTimeoutAsync(3).Wait();

                    Task.Delay(2000).Wait();
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                }
            }
            
            Console.WriteLine("Client connected: " + client.Connected);
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
                return string.CompareOrdinal(userInput, "n") != 0 && string.CompareOrdinal(userInput, "no") != 0;
            }

            return string.CompareOrdinal(userInput, "y") == 0 || string.CompareOrdinal(userInput, "yes") == 0;
        }

        private static string? InputString(string question, string? defaultAnswer, bool allowNull)
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

        private static void Logger(string msg)
        {
            Console.WriteLine(msg);
        }

        private static void MessageReceived(object sender, MessageReceivedEventArgs args)
        {
            var msg = "(null)";
            if (args.Data != null && args.Data.Count > 0) msg = Encoding.UTF8.GetString(args.Data.Array, 0, args.Data.Count);
            Console.WriteLine(args.MessageType.ToString() + " from server: " + msg);
        }

        private static void ServerConnected(object sender, EventArgs args)
        {
            Console.WriteLine("Server connected");
        }

        private static void ServerDisconnected(object sender, EventArgs args)
        {
            Console.WriteLine("Server disconnected");
        }
    }
}
