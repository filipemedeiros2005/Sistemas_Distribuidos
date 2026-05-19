var builder = WebApplication.CreateBuilder(args);

// Add gRPC services to the DI container.
// The PreProcessorService implementation will be added on Day 2.
builder.Services.AddGrpc();

var app = builder.Build();

// Friendly response on the root URL — gRPC requires HTTP/2,
// so this only fires when someone hits the server with a browser.
app.MapGet("/", () =>
    "OneHealth Preprocessor — gRPC endpoints only. Use a gRPC client.");

app.Run();