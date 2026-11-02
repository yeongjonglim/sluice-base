using System.Collections.Concurrent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace IntegrationTests.Supports;

public sealed class CimdStubServer : IAsyncDisposable
{
    public const int Port = 47443;
    public const string LoopbackClientId = "https://localhost:47443/loopback.json";
    public const string WebClientId = "https://localhost:47443/web.json";
    public const string MismatchClientId = "https://localhost:47443/mismatch.json";

    private static readonly Dictionary<string, string> Documents = new()
    {
        ["/loopback.json"] = """{"client_id":"https://localhost:47443/loopback.json","client_name":"Loopback Test Tool","redirect_uris":["http://127.0.0.1/callback"],"token_endpoint_auth_method":"none"}""",
        ["/web.json"] = """{"client_id":"https://localhost:47443/web.json","client_name":"Example Web Tool","redirect_uris":["https://client.example/cb"],"token_endpoint_auth_method":"none"}""",
        ["/mismatch.json"] = """{"client_id":"https://localhost:47443/mismatch.json/","client_name":"Mismatch","redirect_uris":["http://127.0.0.1/callback"],"token_endpoint_auth_method":"none"}""",
    };

    private readonly ConcurrentQueue<string> _requestedPaths = new();
    private WebApplication? _app;

    public IReadOnlyCollection<string> RequestedPaths => _requestedPaths.ToArray();

    public void Clear() => _requestedPaths.Clear();

    public async Task StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options =>
            options.ListenLocalhost(Port, listen => listen.UseHttps()));

        var app = builder.Build();
        app.Run(async context =>
        {
            var path = context.Request.Path.Value ?? "/";
            _requestedPaths.Enqueue(path);

            if (!Documents.TryGetValue(path, out var json))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            context.Response.ContentType = "application/json";
            context.Response.Headers.CacheControl = "max-age=300";
            await context.Response.WriteAsync(json);
        });

        await app.StartAsync();
        _app = app;
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }
}
