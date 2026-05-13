#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Net.Client;
using OneHealth.Grpc.Analysis;

namespace OneHealth.Server
{
    public static class AnalysisClient
    {
        private static readonly Lazy<GrpcChannel> _channel = new(() =>
        {
            var addr = Environment.GetEnvironmentVariable("ANALYSIS_ADDR") ?? "http://localhost:50052";
            if (!addr.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !addr.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                addr = "http://" + addr;
            }
            Console.WriteLine($"[ANALYSIS] gRPC channel -> {addr}");
            return GrpcChannel.ForAddress(addr);
        });

        public static async Task<AnalysisResult?> RunAsync(
            string kind,
            ulong fromUnixTs,
            ulong toUnixTs,
            IEnumerable<string>? dataTypes = null,
            IEnumerable<uint>? sensorIds = null,
            IDictionary<string, string>? options = null)
        {
            var req = new AnalysisRequest
            {
                Kind = kind.ToUpperInvariant(),
                FromUnixTs = fromUnixTs,
                ToUnixTs = toUnixTs,
            };
            if (dataTypes != null) req.DataTypes.AddRange(dataTypes);
            if (sensorIds != null) req.SensorIds.AddRange(sensorIds);
            if (options != null)
                foreach (var (k, v) in options) req.Options[k] = v;

            try
            {
                var client = new AnalysisService.AnalysisServiceClient(_channel.Value);
                return await client.RunAnalysisAsync(req, deadline: DateTime.UtcNow.AddSeconds(10));
            }
            catch (RpcException ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[ANALYSIS] RPC {ex.StatusCode}: {ex.Status.Detail}");
                Console.ResetColor();
                return null;
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[ANALYSIS] {ex.GetType().Name}: {ex.Message}");
                Console.ResetColor();
                return null;
            }
        }
    }
}
