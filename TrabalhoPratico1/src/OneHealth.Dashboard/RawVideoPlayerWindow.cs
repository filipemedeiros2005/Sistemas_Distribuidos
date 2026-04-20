using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace OneHealth.Dashboard
{
    public class RawVideoPlayerWindow : Window
    {
        private Image _imageView;
        private DispatcherTimer _timer;
        private FileStream _fs;
        private byte[] _buffer = new byte[272]; // 16 bytes de header + 256 bytes de cor (frame)
        private int _width = 16;
        private int _height = 16;

        public RawVideoPlayerWindow(string rawFilePath)
        {
            Title = "One Health - Raw Edge/Cloud Video Feed";
            Width = 500;
            Height = 500;
            Background = Brushes.Black;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var stack = new StackPanel { VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
            var text = new TextBlock 
            { 
                Text = "CCTV Feed: " + Path.GetFileName(rawFilePath), 
                Foreground = Brushes.Cyan, 
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                Margin = new Thickness(10)
            };
            
            _imageView = new Image 
            { 
                Width = 400, 
                Height = 400, 
                Stretch = Stretch.UniformToFill // Para ampliar os 16x16 até cobrir
            };

            stack.Children.Add(text);
            stack.Children.Add(_imageView);
            Content = stack;

            if (File.Exists(rawFilePath))
            {
                try {
                    _fs = new FileStream(rawFilePath, FileMode.Open, FileAccess.Read);
                    _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) }; // ~20 FPS para simular playback acelerado
                    _timer.Tick += (s, e) => NextFrame();
                    _timer.Start();
                } catch {
                    text.Text = "Erro a abrir ficheiro bloqueado.";
                }
            } 
            else 
            {
                text.Text = "Ficheiro .raw não encontrado: " + rawFilePath;
            }
        }

        private void NextFrame()
        {
            if (_fs == null || _fs.Read(_buffer, 0, 272) < 272)
            {
                _timer?.Stop();
                _fs?.Dispose();
                
                // Reinicia o player (Looping para a defesa ficar a rolar em background)
                if (_fs != null) {
                    try {
                        _fs.Position = 0;
                        _timer?.Start();
                    } catch {}
                }
                return;
            }

            var bitmap = new WriteableBitmap(new PixelSize(_width, _height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
            using (var locked = bitmap.Lock())
            {
                byte[] bgraData = new byte[256 * 4];
                for (int i = 0; i < 256; i++)
                {
                    byte col = _buffer[16 + i];
                    bgraData[i * 4] = col;       // Blue
                    bgraData[i * 4 + 1] = col;   // Green
                    bgraData[i * 4 + 2] = col;   // Red
                    bgraData[i * 4 + 3] = 255;   // Alpha
                }
                Marshal.Copy(bgraData, 0, locked.Address, bgraData.Length);
            }
            _imageView.Source = bitmap;
        }

        protected override void OnClosed(EventArgs e)
        {
            _timer?.Stop();
            _fs?.Dispose();
            base.OnClosed(e);
        }
    }
}
