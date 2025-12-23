using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using BNLReloadedServer.Database;
using BNLReloadedServer.BaseTypes;

namespace BNLReloadedServer.Servers;

public sealed class StatusHttpServer : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly IRegionServerDatabase _regionDatabase;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly string _prefix;
    private Task? _listenerTask;

    public StatusHttpServer(string prefix, IRegionServerDatabase regionDatabase)
    {
        _prefix = prefix.EndsWith("/") ? prefix : $"{prefix}/";
        _listener.Prefixes.Add(_prefix);
        _regionDatabase = regionDatabase;
    }

    public bool Start()
    {
        if (_listener.IsListening)
        {
            return true;
        }

        try
        {
            _listener.Start();
        }
        catch (HttpListenerException ex)
        {
            Console.WriteLine($"Status HTTP server failed to start: {ex.Message}");
            return false;
        }

        _listenerTask = Task.Run(ListenAsync, _cancellation.Token);
        Console.WriteLine($"Status HTTP server listening on {_prefix}");
        return true;
    }

    public void Stop()
    {
        _cancellation.Cancel();
        if (_listener.IsListening)
        {
            _listener.Stop();
        }

        _listener.Close();

        try
        {
            _listenerTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException ex)
        {
            Console.WriteLine($"Status HTTP server stopped with listener errors: {ex.Flatten().InnerException?.Message}");
        }
    }

    private async Task ListenAsync()
    {
        while (!_cancellation.IsCancellationRequested)
        {
            HttpListenerContext? context = null;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (HttpListenerException ex)
            {
                if (!_cancellation.IsCancellationRequested)
                {
                    Console.WriteLine($"Status HTTP server listener error: {ex.Message}");
                }

                break;
            }

            if (context != null)
            {
                _ = Task.Run(() => HandleRequestAsync(context), _cancellation.Token);
            }
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        try
        {
            var response = new
            {
                onlinePlayers = _regionDatabase.GetOnlinePlayerCount(),
                queues = _regionDatabase.GetQueueCounts(),
                customGames = _regionDatabase.GetCustomGameStatuses()
            };
            var payload = JsonSerializer.Serialize(response);
            var buffer = Encoding.UTF8.GetBytes(payload);
            context.Response.StatusCode = (int)HttpStatusCode.OK;
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = buffer.Length;
            await context.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Status HTTP server request handling failed: {ex.Message}");
            if (context.Response.OutputStream.CanWrite)
            {
                context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            }
        }
        finally
        {
            try
            {
                context.Response.OutputStream.Close();
                context.Response.Close();
            }
            catch
            {
                // Ignore cleanup failures
            }
        }
    }

    public void Dispose()
    {
        Stop();
        _cancellation.Dispose();
    }
}
