#nullable enable
using System;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Net.Client;
using OneHealth.Grpc.Preproc;

namespace OneHealth.Gateway
{
    public static class PreprocessorClient
    {
        public record Result(double Value, bool Dropped, string DropReason);

        private static readonly Lazy<GrpcChannel> _channel = new(() =>
        {
            var addr = Environment.GetEnvironmentVariable("PREPROC_ADDR") ?? "http://localhost:50051";
            if (!addr.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !addr.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                addr = "http://" + addr;
            }
            Console.WriteLine($"[PREPROC] gRPC channel -> {addr}");
            return GrpcChannel.ForAddress(addr);
        });

        public static async Task<Result?> NormalizeAsync(
            uint sensorId, string dataType, double value, ulong unixTs, string zona)
        {
            try
            {
                var client = new PreProcessor.PreProcessorClient(_channel.Value);
                var resp = await client.NormalizeAsync(
                    new RawMeasurement
                    {
                        SensorId = sensorId,
                        DataType = dataType,
                        Value = value,
                        UnixTs = unixTs,
                        Zona = zona
                    },
                    deadline: DateTime.UtcNow.AddSeconds(2));

                return new Result(resp.Value, resp.Dropped, resp.DropReason);
            }
            catch (RpcException ex)
            {
                Console.WriteLine($"[PREPROC] RPC {ex.StatusCode}: {ex.Status.Detail}");
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PREPROC] {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }
    }
}
