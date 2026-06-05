using System;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

namespace OneHealth.Dashboard;

/// <summary>
/// Simulated CCTV live-feed window, ported from TP1 but reading from the
/// Server's TCP video stream instead of a local .raw file. Connects to the
/// stream, sends the 4-byte sensor id, then renders each 16×16 grayscale frame
/// as it arrives. Background colour noise stands in for real video — no codec.
/// </summary>
public sealed class VideoPlayerWindow : Window
{
    private const int FrameWidth = 16;
    private const int FrameHeight = 16;
    private const int PixelCount = FrameWidth * FrameHeight;

    private readonly Image _imageView;
    private readonly CancellationTokenSource _cts = new();

    public VideoPlayerWindow(uint sensorId, string host, int port)
    {
        Title = $"Live feed — sensor {sensorId}";
        Width = 500;
        Height = 540;
        Background = Brushes.Black;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var status = new TextBlock
        {
            Text = $"CCTV feed: sensor {sensorId} ({host}:{port})",
            Foreground = Brushes.Cyan,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            Margin = new Thickness(10)
        };

        _imageView = new Image
        {
            Width = 400,
            Height = 400,
            Stretch = Stretch.UniformToFill
        };

        var stack = new StackPanel { VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        stack.Children.Add(status);
        stack.Children.Add(_imageView);
        Content = stack;

        _ = StreamAsync(sensorId, host, port, status, _cts.Token);
    }

    private async Task StreamAsync(
        uint sensorId, string host, int port, TextBlock status, CancellationToken token)
    {
        var frame = new byte[PixelCount];
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(host, port, token);

            await using var stream = client.GetStream();
            await stream.WriteAsync(BitConverter.GetBytes(sensorId).AsMemory(0, 4), token);

            while (!token.IsCancellationRequested)
            {
                await ReadExactlyAsync(stream, frame, PixelCount, token);
                var bitmap = BuildBitmap(frame);
                await Dispatcher.UIThread.InvokeAsync(() => _imageView.Source = bitmap);
            }
        }
        catch (OperationCanceledException) { /* window closed */ }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
                status.Text = $"Stream unavailable: {ex.Message}");
        }
    }

    private static async Task ReadExactlyAsync(
        NetworkStream stream, byte[] buffer, int count, CancellationToken token)
    {
        var offset = 0;
        while (offset < count)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, count - offset), token);
            if (read == 0) throw new EndOfStreamException("Video stream closed by server.");
            offset += read;
        }
    }

    private static WriteableBitmap BuildBitmap(byte[] grayscale)
    {
        var bitmap = new WriteableBitmap(
            new PixelSize(FrameWidth, FrameHeight),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Opaque);

        using var locked = bitmap.Lock();
        var bgra = new byte[PixelCount * 4];
        for (var i = 0; i < PixelCount; i++)
        {
            var col = grayscale[i];
            bgra[i * 4]     = col;   // B
            bgra[i * 4 + 1] = col;   // G
            bgra[i * 4 + 2] = col;   // R
            bgra[i * 4 + 3] = 255;   // A
        }
        Marshal.Copy(bgra, 0, locked.Address, bgra.Length);
        return bitmap;
    }

    protected override void OnClosed(EventArgs e)
    {
        _cts.Cancel();
        _cts.Dispose();
        base.OnClosed(e);
    }
}
