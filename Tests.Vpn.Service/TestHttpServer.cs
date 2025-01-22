using System.Net;
using System.Text;

namespace Coder.Desktop.Tests.Vpn.Service;

public class TestHttpServer : IDisposable
{
    // IANA suggested range for dynamic or private ports
    private const int MinPort = 49215;
    private const int MaxPort = 65535;
    private const int PortRangeSize = MaxPort - MinPort + 1;

    private readonly CancellationTokenSource _cts = new();
    private readonly Func<HttpListenerContext, Task> _handler;
    private readonly HttpListener _listener;
    private readonly Thread _listenerThread;

    public TestHttpServer(Action<HttpListenerContext> handler) : this(ctx =>
    {
        handler(ctx);
        return Task.CompletedTask;
    })
    {
    }

    public TestHttpServer(Func<HttpListenerContext, Task> handler)
    {
        _handler = handler;

        // Yes, this is the best way to get an unused port using HttpListener.
        // It sucks.
        //
        // This implementation picks a random start point between MinPort and
        // MaxPort, then iterates through the entire range (wrapping around at
        // the end) until it finds a free port.
        var port = 0;
        var random = new Random();
        var startPort = random.Next(MinPort, MaxPort + 1);
        for (var i = 0; i < PortRangeSize; i++)
        {
            port = MinPort + (startPort - MinPort + i) % PortRangeSize;

            var attempt = new HttpListener();
            attempt.Prefixes.Add($"http://localhost:{port}/");
            try
            {
                attempt.Start();
                _listener = attempt;
                break;
            }
            catch
            {
                // Listener disposes itself on failure
            }
        }

        if (_listener == null || port == 0)
            throw new InvalidOperationException("Could not find a free port to listen on");
        BaseUrl = $"http://localhost:{port}";

        _listenerThread = new Thread(() =>
        {
            while (!_cts.Token.IsCancellationRequested)
                try
                {
                    var context = _listener.GetContext();
                    Task.Run(() => HandleRequest(context));
                }
                catch (HttpListenerException) when (_cts.Token.IsCancellationRequested)
                {
                    break;
                }
        });

        _listenerThread.Start();
    }

    public string BaseUrl { get; private set; }

    public void Dispose()
    {
        _cts.Cancel();
        _listener.Stop();
        _listenerThread.Join();
        GC.SuppressFinalize(this);
    }

    private async Task HandleRequest(HttpListenerContext context)
    {
        try
        {
            await _handler(context);
        }
        catch (Exception e)
        {
            await Console.Error.WriteLineAsync($"Exception while serving HTTP request: {e}");
            context.Response.StatusCode = 500;
            var response = Encoding.UTF8.GetBytes($"Internal Server Error: {e.Message}");
            await context.Response.OutputStream.WriteAsync(response);
        }
        finally
        {
            context.Response.Close();
        }
    }
}
