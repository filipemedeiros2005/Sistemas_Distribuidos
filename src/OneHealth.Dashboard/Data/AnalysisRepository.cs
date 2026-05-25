using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using OneHealth.Common.Db;

namespace OneHealth.Dashboard.Data;

/// <summary>
/// Read-only PostgreSQL access for the <c>analysis_results</c> table.
/// Owns an <see cref="NpgsqlDataSource"/> pool reused for the whole
/// Dashboard lifetime, so the periodic ListBox refresh costs are dominated
/// by the query itself (not by connection setup).
///
/// Implements the lazy-load pattern: <see cref="ListRecentAsync"/> returns
/// lightweight rows; <see cref="GetByIdAsync"/> pulls the heavy JSONB
/// payload only when the user explicitly selects an item.
/// </summary>
public sealed class AnalysisRepository : IAsyncDisposable
{
    private readonly NpgsqlDataSource _dataSource;
    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNameCaseInsensitive = true };

    public AnalysisRepository(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Connection string must not be empty.", nameof(connectionString));

        _dataSource = NpgsqlDataSource.Create(connectionString);
    }

    /// <summary>
    /// Returns the most recent analyses without the heavy
    /// <c>series_json</c> payload. Suitable for a 2-second refresh loop.
    /// </summary>
    public async Task<List<AnalysisListItem>> ListRecentAsync(int limit, CancellationToken cancellationToken = default)
    {
        var items = new List<AnalysisListItem>();
        await using var cmd = _dataSource.CreateCommand(AnalysisResultsSchema.ListRecentAnalyses);
        cmd.Parameters.AddWithValue(limit);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new AnalysisListItem
            {
                Id         = reader.GetInt64(0),
                Kind       = reader.GetString(1),
                Summary    = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                ProducedAt = reader.GetDateTime(3),
            });
        }
        return items;
    }

    /// <summary>
    /// Pulls the full payload for one analysis — JSONB <c>metrics</c> and
    /// <c>series_json</c> included — and deserialises them into typed
    /// collections the chart can render directly.
    /// </summary>
    public async Task<AnalysisDetail?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var cmd = _dataSource.CreateCommand(AnalysisResultsSchema.GetAnalysisById);
        cmd.Parameters.AddWithValue(id);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        var metricsJson = reader.IsDBNull(3) ? "{}" : reader.GetString(3);
        var seriesJson  = reader.IsDBNull(4) ? "[]" : reader.GetString(4);

        return new AnalysisDetail
        {
            Id         = reader.GetInt64(0),
            Kind       = reader.GetString(1),
            Summary    = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
            Metrics    = JsonSerializer.Deserialize<Dictionary<string, double>>(metricsJson, JsonOpts) ?? new(),
            Series     = JsonSerializer.Deserialize<List<ChartPoint>>(seriesJson, JsonOpts) ?? new(),
            ProducedAt = reader.GetDateTime(5),
        };
    }

    public ValueTask DisposeAsync() => _dataSource.DisposeAsync();
}

/// <summary>Compact projection used by the history ListBox.</summary>
public sealed class AnalysisListItem
{
    public long Id { get; init; }
    public string Kind { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public DateTime ProducedAt { get; init; }

    /// <summary>How the row renders in the ListBox (single-line, monospaced).</summary>
    public override string ToString() =>
        $"#{Id,-4} {Kind,-12} {ProducedAt.ToLocalTime():HH:mm:ss}";
}

/// <summary>Full payload for one analysis, fetched on-demand for the chart.</summary>
public sealed class AnalysisDetail
{
    public long Id { get; init; }
    public string Kind { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public Dictionary<string, double> Metrics { get; init; } = new();
    public List<ChartPoint> Series { get; init; } = new();
    public DateTime ProducedAt { get; init; }
}

/// <summary>
/// One point in the historical or forecast series. The label groups points
/// into named line series in the chart (e.g. "historical" vs "forecast").
/// </summary>
public sealed class ChartPoint
{
    public long Ts { get; set; }
    public double Value { get; set; }
    public string Label { get; set; } = string.Empty;
}
