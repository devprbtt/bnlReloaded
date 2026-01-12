using System.Text;
using System.Text.Json;
using BNLReloadedServer.Database;
using BNLReloadedServer.ProtocolHelpers;
using BNLReloadedServer.Status;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

namespace BNLReloadedServer.Servers;

public sealed class MasterStatusHttpServer : IAsyncDisposable
{
    private readonly int _port;
    private WebApplication? _app;
    private Task? _runTask;

    public MasterStatusHttpServer(int port)
    {
        _port = port;
    }

    public void Start()
    {
        if (_app != null) return;

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.ListenAnyIP(_port);
        });

        var app = builder.Build();
        app.MapGet("/status", async context =>
        {
            var snapshot = BuildMasterSnapshot();
            var json = JsonSerializer.Serialize(snapshot, JsonHelper.DefaultSerializerSettings);
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(json);
        });

        app.MapPost("/match/pause", async context =>
        {
            if (!TryAuthorize(context))
            {
                return;
            }

            var instanceId = context.Request.Query["instanceId"].ToString();
            if (string.IsNullOrWhiteSpace(instanceId))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsync("instanceId is required");
                return;
            }

            bool? paused = null;
            var pausedRaw = context.Request.Query["paused"].ToString();
            if (!string.IsNullOrWhiteSpace(pausedRaw))
            {
                if (!bool.TryParse(pausedRaw, out var parsed))
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    await context.Response.WriteAsync("paused must be true or false");
                    return;
                }

                paused = parsed;
            }

            var instance = Databases.RegionServerDatabase.GetGameInstanceById(instanceId);
            if (instance == null)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                await context.Response.WriteAsync("match not found");
                return;
            }

            var requestedPause = paused ?? !instance.IsMatchPaused();
            var accepted = instance.SetMatchPaused(requestedPause);
            var response = new
            {
                instanceId,
                requestedPaused = requestedPause,
                accepted
            };

            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(response, JsonHelper.DefaultSerializerSettings));
        });

        app.MapGet("/", async context =>
        {
            await context.Response.WriteAsync("BNL Reloaded master status service");
        });

        _app = app;
        _runTask = app.RunAsync();
        Console.WriteLine($"Master status HTTP server listening on port {_port}");
    }

    public async Task StopAsync()
    {
        if (_app == null) return;
        await _app.StopAsync();
        if (_runTask != null)
        {
            await _runTask;
        }
        await _app.DisposeAsync();
        _app = null;
        _runTask = null;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }

    private static MasterStatusSnapshot BuildMasterSnapshot()
    {
        var regions = Databases.MasterServerDatabase.GetRegionStatusSnapshots()
            .ToDictionary(snapshot => snapshot.RegionId, snapshot => snapshot);

        if (Databases.ConfigDatabase.IsMaster())
        {
            var localSnapshot = Databases.RegionServerDatabase.BuildStatusSnapshot();
            var masterSnapshot = localSnapshot with
            {
                RegionId = "master",
                RegionName = Databases.ConfigDatabase.GetRegionInfo().Name?.Text,
                RegionHost = Databases.ConfigDatabase.MasterPublicHost()
            };
            regions["master"] = masterSnapshot;
        }

        return new MasterStatusSnapshot
        {
            GeneratedAt = DateTimeOffset.UtcNow,
            Regions = regions.Values.OrderBy(snapshot => snapshot.RegionId).ToList()
        };
    }

    private static bool TryAuthorize(HttpContext context)
    {
        var username = Databases.ConfigDatabase.StatusHttpUsername();
        var password = Databases.ConfigDatabase.StatusHttpPassword();
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            return true;
        }

        var auth = context.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(auth) || !auth.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.Headers.WWWAuthenticate = "Basic realm=\"BNL Reloaded\"";
            return false;
        }

        var encoded = auth["Basic ".Length..].Trim();
        string decoded;
        try
        {
            decoded = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
        }
        catch (FormatException)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.Headers.WWWAuthenticate = "Basic realm=\"BNL Reloaded\"";
            return false;
        }

        var parts = decoded.Split(':', 2);
        if (parts.Length != 2 ||
            !string.Equals(parts[0], username, StringComparison.Ordinal) ||
            !string.Equals(parts[1], password, StringComparison.Ordinal))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.Headers.WWWAuthenticate = "Basic realm=\"BNL Reloaded\"";
            return false;
        }

        return true;
    }
}
