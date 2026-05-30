using System.Net;
using System.Net.Sockets;

namespace OneHealth.Server.Video;

/// <summary>
/// Serves a simulated "CCTV" live feed over TCP, in the spirit of the TP1
/// edge-video proxy. A client (the Dashboard) connects, sends the 4-byte
/// sensor id it wants to watch, and receives an endless stream of fixed-size
/// grayscale frames (16×16, one byte per pixel).
///
/// The pixels are random noise — exactly as TP1 did — so no real codec is
/// involved; the point is to demonstrate a separate streaming transport
/// (raw TCP sockets) alongside the telemetry pipeline, not actual video.
/// </summary>
public sealed class VideoStreamService
{
    public const int DefaultPort = 9000;

    /// <summary>Frame geometry: 16×16 pixels, one byte (grayscale) each.</summary>
    public const int FrameWidth = 16;
    public const int FrameHeight = 16;
    public const int FrameSize = FrameWidth * FrameHeight;

    private static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(100);

    private readonly int _port;

    public VideoStreamService(int port = DefaultPort)
    {
        _port = port;
    }

    /// <summary>
    /// Accepts clients until cancelled. Each client is handled on its own task
    /// so several Dashboards (or several sensor feeds) can stream at once.
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var listener = new TcpListener(IPAddress.Loopback, _port);
        listener.Start();
        Console.WriteLine($"[VIDEO] Live feed serving on tcp://127.0.0.1:{_port}");

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(cancellationToken);
                _ = HandleClientAsync(client, cancellationToken);
            }
        }
        catch (OperationCanceledException) { /* expected on shutdown */ }
        finally
        {
            listener.Stop();
            Console.WriteLine("[VIDEO] Live feed stopped.");
        }
    }

    private static async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        // Each connected client streams independently, with its own RNG so the
        // feeds look different from one another.
        var rng = new Random();
        var frame = new byte[FrameSize];

        try
        {
            using (client)
            await using (var stream = client.GetStream())
            {
                // Read the 4-byte sensor id the client wants to watch. We don't
                // serve different content per sensor (the noise is the same kind
                // either way), but we honour the handshake so the wire protocol
                // mirrors a real per-sensor feed.
                var idBuffer = new byte[4];
                var read = await stream.ReadAsync(idBuffer.AsMemory(0, 4), cancellationToken);
                if (read < 4) return;

                var sensorId = BitConverter.ToUInt32(idBuffer, 0);
                Console.WriteLine($"[VIDEO] Client subscribed to sensor {sensorId}");

                while (!cancellationToken.IsCancellationRequested)
                {
                    rng.NextBytes(frame);
                    // Clamp into the 30..100 band TP1 used, for a muted look.
                    for (var i = 0; i < frame.Length; i++)
                        frame[i] = (byte)(30 + frame[i] % 70);

                    await stream.WriteAsync(frame.AsMemory(0, FrameSize), cancellationToken);
                    await Task.Delay(FrameInterval, cancellationToken);
                }
            }
        }
        catch (OperationCanceledException) { /* shutdown */ }
        catch (IOException) { /* client disconnected — normal when the window closes */ }
    }
}
