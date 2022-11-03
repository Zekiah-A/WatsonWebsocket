using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using WatsonWebsocket;

namespace Test.Server
{
    internal class Program
    {
        private static string serverIp = "localhost";
        private static int serverPort;
        private static bool ssl;
        private static readonly bool AcceptInvalidCertificates = true;
        private static WatsonWsServer server;
        private static string lastIpPort;

        private static void Main(string[] args)
        {
            serverIp = InputString("Server IP:", "localhost", true);
            serverPort = InputInteger("Server port:", 9000, true, true);
            ssl = InputBoolean("Use SSL:", false);

            InitializeServer();
            // InitializeServerMultiple();
            Console.WriteLine("Please manually start the server");

            var runForever = true;
            while (runForever)
            {
                Console.Write("Command [? for help]: ");
                var userInput = Console.ReadLine()?.Trim();
                if (string.IsNullOrEmpty(userInput)) continue;
                var splitInput = userInput.Split(new string[] { " " }, 2, StringSplitOptions.None);
                string ipPort = null;
                var success = false;

                switch (splitInput[0])
                {
                    case "?":
                        Console.WriteLine("Available commands:");
                        Console.WriteLine("  ?                            help (this menu)");
                        Console.WriteLine("  q                            quit");
                        Console.WriteLine("  cls                          clear screen");
                        Console.WriteLine("  dispose                      dispose of the server");
                        Console.WriteLine("  reinit                       reinitialize the server");
                        Console.WriteLine("  start                        start accepting new connections (listening: " + server.IsListening + ")");
                        Console.WriteLine("  stop                         stop accepting new connections");
                        Console.WriteLine("  list                         list clients");
                        Console.WriteLine("  stats                        display server statistics");
                        Console.WriteLine("  send ip:port text message    send text to client");
                        Console.WriteLine("  send ip:port bytes message   send binary data to client");
                        Console.WriteLine("  kill ip:port                 disconnect a client");
                        break;

                    case "q":
                        runForever = false;
                        break;

                    case "cls":
                        Console.Clear();
                        break;

                    case "dispose":
                        server.Dispose();
                        break;

                    case "reinit":
                        InitializeServer();
                        break;

                    case "start":
                        StartServer();
                        break;

                    case "stop":
                        server.Stop();
                        break;

                    case "list":
                    {
                        if (server.Clients.Count > 0)
                        {
                            Console.WriteLine("Clients");
                            foreach (var client in server.Clients)
                            {
                                Console.WriteLine("  " + client.IpPort);
                            }
                        }
                        else
                        {
                            Console.WriteLine("[No clients connected]");
                        } 
                        break;
                    }
                    case "stats":
                        Console.WriteLine(server.Stats.ToString());
                        break;

                    case "send":
                    {
                    
                        if (splitInput.Length != 2) break;
                        splitInput = splitInput[1].Split(new string[] {" "}, 3, StringSplitOptions.None);
                        if (splitInput.Length != 3) break;
                        ipPort = splitInput[0].Equals("last") ? lastIpPort : splitInput[0];
                        if (string.IsNullOrEmpty(splitInput[2])) break;

                        var client = server.GetClientFromIpPort(ipPort);
                        if (client is null) return;

                        if (splitInput[1].Equals("text")) success = server.SendAsync(client, splitInput[2]).Result;
                        else if (splitInput[1].Equals("bytes"))
                        {
                            var data = Encoding.UTF8.GetBytes(splitInput[2]);
                            success = server.SendAsync(client, data).Result;
                        }
                        else break;

                        Console.WriteLine(!success ? "Failed" : "Success");
                        break;
                    }
                    case "kill":
                        if (splitInput.Length != 2) break;
                        server.DisconnectClient(server.GetClientFromIpPort(splitInput[1]));
                        break;

                    default:
                        Console.WriteLine("Unknown command: " + userInput);
                        break;
                }
            }
        }

        private static void InitializeServer()
        {
            server = new WatsonWsServer(serverIp, serverPort, ssl);            
            server.AcceptInvalidCertificates = AcceptInvalidCertificates;
            server.ClientConnected += ClientConnected;
            server.ClientDisconnected += ClientDisconnected;
            server.MessageReceived += MessageReceived;
            server.Logger = Logger;
            server.HttpHandler = HttpHandler;
        }

        private static void InitializeServerMultiple()
        {
            // original constructor
            var hostnames = new List<string>
            {
                "192.168.1.163",
                "127.0.0.1"
            };

            server = new WatsonWsServer(hostnames, serverPort, ssl);

            // URI-based constructor
            // if (_Ssl) _Server = new WatsonWsServer(new Uri("https://" + _ServerIp + ":" + _ServerPort));
            // else _Server = new WatsonWsServer(new Uri("http://" + _ServerIp + ":" + _ServerPort));

            server.ClientConnected += ClientConnected;
            server.ClientDisconnected += ClientDisconnected;
            server.MessageReceived += MessageReceived;
            server.Logger = Logger;
            server.HttpHandler = HttpHandler;
        }

        private static async void StartServer()
        {                         
            // _Server.Start();
            await server.StartAsync();
            Console.WriteLine("Server is listening: " + server.IsListening);
        }

        private static void Logger(string msg)
        {
            Console.WriteLine(msg);
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

        private static void ClientConnected(object sender, ClientConnectedEventArgs args) 
        {
            Console.WriteLine("Client " + args.Client.IpPort + " connected using URL " + args.HttpRequest.RawUrl);
            lastIpPort = args.Client.IpPort;

            if (args.HttpRequest.Cookies != null && args.HttpRequest.Cookies.Count > 0)
            {
                Console.WriteLine(args.HttpRequest.Cookies.Count + " cookie(s) present:");
                foreach (Cookie cookie in args.HttpRequest.Cookies)
                {
                    Console.WriteLine("| " + cookie.Name + ": " + cookie.Value);
                }
            }
        }

        private static void ClientDisconnected(object sender, ClientDisconnectedEventArgs args)
        {
            Console.WriteLine("Client disconnected: " + args.Client.IpPort);
        }

        private static void MessageReceived(object sender, MessageReceivedEventArgs args)
        {
            var msg = "(null)";
            if (args.Data != null && args.Data.Count > 0) msg = Encoding.UTF8.GetString(args.Data.Array, 0, args.Data.Count);
            Console.WriteLine(args.MessageType + " from " + args.Client.IpPort + ": " + msg);
        }

        private static void HttpHandler(HttpListenerContext ctx)
        { 
            var req = ctx.Request;
            string contents = null;
            using (var stream = req.InputStream)
            {
                using (var readStream = new StreamReader(stream, Encoding.UTF8))
                {
                    contents = readStream.ReadToEnd();
                }
            }

            Console.WriteLine("Non-websocket request received for: " + req.HttpMethod.ToString() + " " + req.RawUrl);
            if (req.Headers != null && req.Headers.Count > 0)
            {
                Console.WriteLine("Headers:"); 
                var items = req.Headers.AllKeys.SelectMany(req.Headers.GetValues, (k, v) => new { key = k, value = v });
                foreach (var item in items)
                {
                    Console.WriteLine("  {0}: {1}", item.key, item.value);
                }
            }

            if (!string.IsNullOrEmpty(contents))
            {
                Console.WriteLine("Request body:");
                Console.WriteLine(contents);
            }
        }
    }
}