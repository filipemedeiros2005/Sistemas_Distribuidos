#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Npgsql;
using NpgsqlTypes;
using OneHealth.Grpc.Analysis;

namespace OneHealth.Server
{
    public static class AnalysisCoordinator
    {
        public const int LISTEN_PORT = 5006;

        public static async Task StartAsync(string dbConnection)
        {
            var listener = new TcpListener(IPAddress.Loopback, LISTEN_PORT);
            listener.Start();
            Console.WriteLine($"[ANALYSIS] Coordenador TCP a ouvir em :{LISTEN_PORT}.");

            while (true)
            {
                TcpClient client;
                try { client = await listener.AcceptTcpClientAsync(); }
                catch { continue; }
                _ = Task.Run(() => HandleAsync(client, dbConnection));
            }
        }

        private static async Task HandleAsync(TcpClient client, string dbConnection)
        {
            string remote = client.Client.RemoteEndPoint?.ToString() ?? "?";
            using (client)
            using (var stream = client.GetStream())
            using (var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" })
            {
                string? line;
                try { line = await reader.ReadLineAsync(); }
                catch { return; }
                if (string.IsNullOrWhiteSpace(line))
                {
                    await writer.WriteLineAsync("ERR empty request");
                    return;
                }

                Console.WriteLine($"[ANALYSIS] Pedido de {remote}: {line}");

                var fields = ParseFields(line);
                if (!fields.TryGetValue("KIND", out var kind) || string.IsNullOrWhiteSpace(kind))
                {
                    await writer.WriteLineAsync("ERR missing KIND");
                    return;
                }

                int windowSeconds = ParseWindowSeconds(fields.GetValueOrDefault("WINDOW", "5m"));
                ulong toTs = (ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                ulong fromTs = toTs > (ulong)windowSeconds ? toTs - (ulong)windowSeconds : 0;

                var dataTypes = SplitCsv(fields.GetValueOrDefault("TYPES"));
                var sensorIds = SplitCsvUInt(fields.GetValueOrDefault("SENSORS"));

                if (sensorIds.Count == 0 && fields.TryGetValue("ZONA", out var zona) && !string.IsNullOrWhiteSpace(zona))
                {
                    sensorIds = ResolveZone(zona);
                    if (sensorIds.Count == 0)
                    {
                        await writer.WriteLineAsync($"ERR zone '{zona}' resolves to no sensors");
                        return;
                    }
                    Console.WriteLine($"[ANALYSIS] Zona '{zona}' -> sensores [{string.Join(",", sensorIds)}]");
                }

                var options = new Dictionary<string, string>();
                if (fields.TryGetValue("HORIZON", out var horizon)) options["horizon"] = horizon;

                var result = await AnalysisClient.RunAsync(kind, fromTs, toTs, dataTypes, sensorIds, options);
                if (result == null)
                {
                    await writer.WriteLineAsync("ERR analysis service unavailable");
                    return;
                }

                int id;
                try
                {
                    id = await PersistAsync(dbConnection, result, fromTs, toTs);
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"[ANALYSIS] Falha de persistência: {ex.Message}");
                    Console.ResetColor();
                    await writer.WriteLineAsync($"ERR persist failed: {ex.Message}");
                    return;
                }

                Console.WriteLine($"[ANALYSIS] {result.Kind} -> id={id}, summary=\"{result.SummaryText}\"");
                await writer.WriteLineAsync($"OK {id}");
            }
        }

        private static Dictionary<string, string> ParseFields(string line)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var part in line.Split('|', StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = part.IndexOf('=');
                if (eq <= 0) continue;
                dict[part.Substring(0, eq).Trim()] = part.Substring(eq + 1).Trim();
            }
            return dict;
        }

        private static int ParseWindowSeconds(string spec)
        {
            if (string.IsNullOrWhiteSpace(spec)) return 300;
            char unit = char.ToLowerInvariant(spec[^1]);
            string num = char.IsLetter(unit) ? spec[..^1] : spec;
            if (!int.TryParse(num, out var n) || n <= 0) return 300;
            return unit switch
            {
                's' => n,
                'm' => n * 60,
                'h' => n * 3600,
                _ => n,
            };
        }

        private static List<string> SplitCsv(string? csv)
        {
            if (string.IsNullOrWhiteSpace(csv)) return new();
            return csv.Split(',', StringSplitOptions.RemoveEmptyEntries)
                      .Select(s => s.Trim())
                      .Where(s => s.Length > 0)
                      .ToList();
        }

        private static List<uint> SplitCsvUInt(string? csv)
        {
            if (string.IsNullOrWhiteSpace(csv)) return new();
            var list = new List<uint>();
            foreach (var s in csv.Split(',', StringSplitOptions.RemoveEmptyEntries))
                if (uint.TryParse(s.Trim(), out var v)) list.Add(v);
            return list;
        }

        private static List<uint> ResolveZone(string zona)
        {
            string baseDir = AppContext.BaseDirectory;
            string dataRoot = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "..", "data"));
            if (!Directory.Exists(dataRoot))
                dataRoot = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "data"));
            string cfgDir = Path.Combine(dataRoot, "gateway_configs");
            if (!Directory.Exists(cfgDir)) return new();

            var ids = new List<uint>();
            foreach (var file in Directory.GetFiles(cfgDir, "gw_*.csv"))
            {
                foreach (var raw in File.ReadAllLines(file))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    var parts = line.Split(':');
                    if (parts.Length < 5) continue;
                    if (!uint.TryParse(parts[0], out var id)) continue;
                    if (string.Equals(parts[2], zona, StringComparison.OrdinalIgnoreCase)) ids.Add(id);
                }
            }
            return ids.Distinct().ToList();
        }

        private static async Task<int> PersistAsync(string dbConnection, AnalysisResult result, ulong fromTs, ulong toTs)
        {
            var metricsJson = JsonSerializer.Serialize(
                result.Metrics.ToDictionary(kv => kv.Key, kv => kv.Value));
            var seriesJson = JsonSerializer.Serialize(
                result.Series.Select(p => new { ts = p.Ts, value = p.Value, label = p.Label }).ToArray());

            await using var conn = new NpgsqlConnection(dbConnection);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(@"
                INSERT INTO analysis_results (kind, from_ts, to_ts, produced_at, summary, metrics, series)
                VALUES (@kind, @from_ts, @to_ts, @produced_at, @summary, @metrics, @series)
                RETURNING id", conn);
            cmd.Parameters.AddWithValue("kind", result.Kind);
            cmd.Parameters.AddWithValue("from_ts", DateTimeOffset.FromUnixTimeSeconds((long)fromTs).UtcDateTime);
            cmd.Parameters.AddWithValue("to_ts", DateTimeOffset.FromUnixTimeSeconds((long)toTs).UtcDateTime);
            cmd.Parameters.AddWithValue("produced_at", DateTimeOffset.FromUnixTimeSeconds((long)result.ProducedUnixTs).UtcDateTime);
            cmd.Parameters.AddWithValue("summary", result.SummaryText);
            cmd.Parameters.Add(new NpgsqlParameter("metrics", NpgsqlDbType.Jsonb) { Value = metricsJson });
            cmd.Parameters.Add(new NpgsqlParameter("series", NpgsqlDbType.Jsonb) { Value = seriesJson });
            return (int)(await cmd.ExecuteScalarAsync())!;
        }
    }
}
