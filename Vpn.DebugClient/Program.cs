using System.IO.Pipes;
using System.Net.Sockets;
using Coder.Desktop.Vpn.Proto;

namespace Coder.Desktop.Vpn.DebugClient;

public static class Program
{
    private static Speaker<ClientMessage, ServiceMessage>? _speaker;

    private static string? _coderUrl;
    private static string? _apiToken;

    public static void Main()
    {
        Console.WriteLine("Type 'exit' to exit the program");
        Console.WriteLine("Type 'connect' to connect to the service");
        Console.WriteLine("Type 'disconnect' to disconnect from the service");
        Console.WriteLine("Type 'configure' to set the parameters");
        Console.WriteLine("Type 'start' to send a start command with the current parameters");
        Console.WriteLine("Type 'stop' to send a stop command");
        while (true)
        {
            Console.Write("> ");
            var input = Console.ReadLine()?.Trim();
            try
            {
                switch (input)
                {
                    case "exit":
                        return;
                    case "connect":
                        Connect();
                        break;
                    case "disconnect":
                        Disconnect();
                        break;
                    case "configure":
                        Configure();
                        break;
                    case "start":
                        Start();
                        break;
                    case "stop":
                        Stop();
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex}");
            }
        }
    }

    private static void Connect()
    {
        Stream stream;
        if (OperatingSystem.IsWindows())
        {
            var client = new NamedPipeClientStream(".", "Coder.Desktop.Vpn", PipeDirection.InOut, PipeOptions.Asynchronous);
            client.Connect();
            stream = client;
        }
        else
        {
            var socketPath = "/run/coder-desktop/vpn.sock";
            var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            socket.Connect(new UnixDomainSocketEndPoint(socketPath));
            stream = new NetworkStream(socket, ownsSocket: true);
        }
        Console.WriteLine("Connected to RPC server.");

        _speaker = new Speaker<ClientMessage, ServiceMessage>(stream);
        _speaker.Receive += message => { Console.WriteLine($"Received({message.Message.MsgCase}: {message.Message}"); };
        _speaker.Error += exception =>
        {
            Console.WriteLine($"Error: {exception}");
            Disconnect();
        };
        _speaker.StartAsync().Wait();
        Console.WriteLine("Speaker started.");
    }

    private static void Disconnect()
    {
        _speaker?.DisposeAsync().AsTask().Wait();
        _speaker = null;
        Console.WriteLine("Disconnected from named pipe");
    }

    private static void Configure()
    {
        Console.Write("Coder URL: ");
        _coderUrl = Console.ReadLine()?.Trim();
        Console.Write("API Token: ");
        _apiToken = Console.ReadLine()?.Trim();
    }

    private static void Start()
    {
        if (_speaker is null)
        {
            Console.WriteLine("Not connected to Coder.Desktop.Vpn.");
            return;
        }

        var message = new ClientMessage
        {
            Start = new StartRequest
            {
                CoderUrl = _coderUrl,
                ApiToken = _apiToken,
            },
        };
        Console.WriteLine("Sending start message...");
        var sendTask = _speaker.SendRequestAwaitReply(message).AsTask();
        Console.WriteLine("Start message sent, awaiting reply.");
        sendTask.Wait();
        Console.WriteLine($"Received reply: {sendTask.Result.Message}");
    }

    private static void Stop()
    {
        if (_speaker is null)
        {
            Console.WriteLine("Not connected to Coder.Desktop.Vpn.");
            return;
        }

        var message = new ClientMessage
        {
            Stop = new StopRequest(),
        };
        Console.WriteLine("Sending stop message...");
        var sendTask = _speaker.SendRequestAwaitReply(message);
        Console.WriteLine("Stop message sent, awaiting reply.");
        var reply = sendTask.AsTask().Result;
        Console.WriteLine($"Received reply: {reply.Message}");
    }
}
