using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using OneHealth.Common.Db;
using OneHealth.Grpc.Analysis;

namespace OneHealth.Server.Persistence;

/// <summary>
/// Writes <see cref="AnalysisResult"/> rows into the PostgreSQL
/// <c>analysis_results</c> table after each successful gRPC call to the
/// Python service. Owns its own <see cref="NpgsqlDataSource"/> pool so the
/// writer is independent from the telemetry-ingestion writer.
///
/// Maps the gRPC payload into two JSONB columns (<c>metrics</c> and
/// <c>series_json</c>) using <see cref="System.Text.Json"/>; we never store
/// the Protobuf binary representation, which would be opaque to the
/// Dashboard and to SQL ad-hoc queries.
/// </summary>
public sealed class AnalysisResultWriter : IAsyncDisposable
{
    private readonly NpgsqlDataSource _dataSource;

    public AnalysisResultWriter(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Connection string must not be empty.", nameof(connectionString));

        _dataSource = NpgsqlDataSource.Create(connectionString);
    }

    /// <summary>Ensures the table exists. Idempotent; safe to call on every boot.</summary>
    public async Task InitSchemaAsync(CancellationToken cancellationToken = default)
    {
        await using var cmd = _dataSource.CreateCommand(AnalysisResultsSchema.CreateTable);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Persists a single analysis result and returns the row id (so the
    /// Coordinator can echo it back to the Dashboard).
    /// </summary>
    public async Task<long> InsertAsync(AnalysisResult result, CancellationToken cancellationToken = default)
    {
        var metricsJson = SerializeMetrics(result);
        var seriesJson  = SerializeSeries(result);

        await using var cmd = _dataSource.CreateCommand(AnalysisResultsSchema.InsertAnalysisResult);
        cmd.Parameters.AddWithValue(result.Kind ?? "UNKNOWN");
        cmd.Parameters.AddWithValue(result.SummaryText ?? string.Empty);
        cmd.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Jsonb, Value = metricsJson });
        cmd.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Jsonb, Value = seriesJson });

        var idObj = await cmd.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(idObj);
    }

    private static string SerializeMetrics(AnalysisResult result)
    {
        var dict = new Dictionary<string, double>();
        foreach (var kv in result.Metrics)
            dict[kv.Key] = kv.Value;
        return JsonSerializer.Serialize(dict);
    }

    private static string SerializeSeries(AnalysisResult result)
    {
        var arr = result.Series.Select(p => new
        {
            ts    = (long)p.Ts,
            value = p.Value,
            label = p.Label ?? string.Empty,
        });
        return JsonSerializer.Serialize(arr);
    }

    public ValueTask DisposeAsync() => _dataSource.DisposeAsync();
}
