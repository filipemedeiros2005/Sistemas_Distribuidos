using Grpc.Core;
using Grpc.Net.Client;
using OneHealth.Grpc.Analysis;
using OneHealth.Server.Coordinator;

namespace OneHealth.Server.Analysis;

/// <summary>
/// Singleton wrapper over the generated gRPC client for the Python analysis
/// service. Holds a long-lived <see cref="GrpcChannel"/>.
///
/// During Day 4 the Python service does not yet exist, so calls fail with
/// <see cref="StatusCode.Unavailable"/>. <see cref="RunAnalysisAsync"/>
/// catches the exception and returns <c>null</c>, which the coordinator
/// reports as <c>ANALYSIS_UNAVAILABLE</c> to the Dashboard.
/// </summary>
public sealed class AnalysisClient : IDisposable
{
    private readonly GrpcChannel _channel;
    private readonly AnalysisService.AnalysisServiceClient _client;
    private readonly TimeSpan _deadline;

    public AnalysisClient(string address = "http://localhost:50052", TimeSpan? deadline = null)
    {
        if (string.IsNullOrWhiteSpace(address))
            throw new ArgumentException("Address must not be empty.", nameof(address));

        _channel = GrpcChannel.ForAddress(address);
        _client  = new AnalysisService.AnalysisServiceClient(_channel);
        _deadline = deadline ?? TimeSpan.FromSeconds(10);
    }

    /// <summary>
    /// Sends the resolved query to Python. Returns null if the service is
    /// unreachable, slow, or returns a transient error — the caller chooses
    /// how to surface that to the user.
    /// </summary>
    public async Task<AnalysisResult?> RunAnalysisAsync(
        AnalysisQuery query,
        IReadOnlyList<uint> sensorIds,
        CancellationToken cancellationToken = default)
    {
        var nowMs = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var windowMs = (ulong)TimeSpan.FromMinutes(query.WindowMinutes).TotalMilliseconds;

        var request = new AnalysisRequest
        {
            Kind        = query.Kind,
            FromUnixTs  = nowMs > windowMs ? nowMs - windowMs : 0,
            ToUnixTs    = nowMs,
        };
        request.DataTypes.AddRange(query.DataTypes);
        request.SensorIds.AddRange(sensorIds);
        if (query.Horizon.HasValue)
            request.Options["HORIZON"] = query.Horizon.Value.ToString();

        try
        {
            return await _client.RunAnalysisAsync(
                request,
                deadline: DateTime.UtcNow.Add(_deadline),
                cancellationToken: cancellationToken);
        }
        catch (RpcException ex)
        {
            Console.Error.WriteLine(
                $"[ANALYSIS-CLIENT] gRPC failed ({ex.StatusCode}): {ex.Status.Detail}");
            return null;
        }
    }

    public void Dispose() => _channel.Dispose();
}
