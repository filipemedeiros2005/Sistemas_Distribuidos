using Microsoft.AspNetCore.Server.Kestrel.Core;
using OneHealth.Preprocessor.Authorization;
using OneHealth.Preprocessor.Services;

var builder = WebApplication.CreateBuilder(args);

// Force HTTP/2-only on the fixed gRPC port — works the same whether launched
// via `dotnet run`, the published binary, or systemd, independent of cwd.
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenLocalhost(50051, listen => listen.Protocols = HttpProtocols.Http2);
});

// Authorization cache: queries the PostgreSQL `sensors` table populated by
// the Gateway. Singleton so the in-memory cache and Mutex survive across
// concurrent Normalize calls.
var pgConn = Environment.GetEnvironmentVariable("ONEHEALTH_PG_CONN")
             ?? $"Host=localhost;Port=5432;Database=onehealth;Username={Environment.UserName}";
builder.Services.AddSingleton(new SensorAuthorizationCache(pgConn));

builder.Services.AddGrpc();

var app = builder.Build();

app.MapGrpcService<PreProcessorService>();

// Friendly response on the root URL — gRPC requires HTTP/2,
// so this only fires when someone hits the server with a browser.
app.MapGet("/", () =>
    "OneHealth Preprocessor — gRPC endpoints only. Use a gRPC client.");

app.Run();