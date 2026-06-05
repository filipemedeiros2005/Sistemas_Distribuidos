namespace OneHealth.Common.Db;

/// <summary>
/// SQL DDL and DML for the <c>analysis_results</c> table — the historical
/// log of every analysis the Python service has returned. Written by the
/// Server's <c>AnalysisCoordinator</c> after a successful gRPC call; read
/// by the Dashboard for the "Analysis history" ListBox and on-demand
/// chart rendering.
///
/// The <c>metrics</c> and <c>series_json</c> columns are <c>JSONB</c> so
/// the wire format (gRPC <c>map&lt;string,double&gt;</c> and
/// <c>repeated SeriesPoint</c>) survives without a relational explosion.
/// </summary>
public static class AnalysisResultsSchema
{
    public const string CreateTable = @"
        CREATE TABLE IF NOT EXISTS analysis_results (
            id          BIGSERIAL    PRIMARY KEY,
            kind        VARCHAR(40)  NOT NULL,
            summary     TEXT,
            metrics     JSONB,
            series_json JSONB,
            produced_at TIMESTAMPTZ  NOT NULL DEFAULT NOW()
        );
        CREATE INDEX IF NOT EXISTS idx_analysis_kind_time
            ON analysis_results(kind, produced_at DESC);";

    /// <summary>
    /// Insert performed by the Server after a Python analysis returns. The
    /// <c>RETURNING id</c> clause lets the Dashboard reference a specific
    /// historical run without re-querying.
    /// </summary>
    public const string InsertAnalysisResult = @"
        INSERT INTO analysis_results (kind, summary, metrics, series_json)
        VALUES ($1, $2, $3::jsonb, $4::jsonb)
        RETURNING id;";

    /// <summary>
    /// Lightweight listing query — does NOT pull the heavy
    /// <c>series_json</c>. Used by the Dashboard's periodic refresh of the
    /// history ListBox.
    /// </summary>
    public const string ListRecentAnalyses = @"
        SELECT id, kind, summary, produced_at
        FROM analysis_results
        ORDER BY produced_at DESC
        LIMIT $1;";

    /// <summary>
    /// On-demand fetch of one analysis's full payload — triggered only when
    /// the user selects a row in the ListBox (lazy-load pattern).
    /// </summary>
    public const string GetAnalysisById = @"
        SELECT id, kind, summary, metrics, series_json, produced_at
        FROM analysis_results
        WHERE id = $1;";
}
