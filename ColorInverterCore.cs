using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Collections.Generic;
using System.Linq;
using WinForms = System.Windows.Forms;

namespace ColorInverter
{
    public class ColorInverterCore
    {
        private readonly Rectangle monitorRect;
        private readonly VideoWindowDetector videoDetector;
        private bool running;
        private Window? overlayWindow;
        private bool inversionActive;
        private CancellationTokenSource? cancellationTokenSource;

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll")]
        private static extern bool BitBlt(IntPtr hdc, int nXDest, int nYDest, int nWidth, int nHeight, IntPtr hdcSrc, int nXSrc, int nYSrc, int dwRop);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr hdc);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(System.Drawing.Point pt, uint dwFlags);

        [DllImport("shcore.dll")]
        private static extern int GetDpiForMonitor(IntPtr hmonitor, DpiType dpiType, out uint dpiX, out uint dpiY);

        private const int SRCCOPY = 0x00CC0020;
        private const uint MONITOR_DEFAULTTONEAREST = 2;

        private enum DpiType
        {
            Effective = 0,
            Angular = 1,
            Raw = 2,
        }

        public ColorInverterCore(Rectangle monitorRect, VideoWindowDetector videoDetector)
        {
            this.monitorRect = monitorRect;
            this.videoDetector = videoDetector;
        }

        public void CreateOverlayWindow()
        {
            // Create overlay window - semi-transparent for testing
            overlayWindow = new Window
            {
                Title = "Color Inverter Overlay",
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                AllowsTransparency = true,
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(128, 255, 0, 0)), // Semi-transparent red for testing
                Topmost = true,
                ShowInTaskbar = false,
                WindowState = WindowState.Normal
            };

            // Position on the correct monitor using Screen.AllScreens
            var targetScreenFound = PositionOnTargetMonitor();

            // Add a simple text label so we can see the window
            var textBlock = new System.Windows.Controls.TextBlock
            {
                Text = $"OVERLAY WINDOW TEST\\nMonitor bounds: {monitorRect.X}, {monitorRect.Y}, {monitorRect.Width}x{monitorRect.Height}\\nTarget screen found: {targetScreenFound}",
                FontSize = 16,
                Foreground = System.Windows.Media.Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            
            overlayWindow.Content = textBlock;
            
            // Handle window close to reset state
            overlayWindow.Closed += (sender, e) =>
            {
                overlayWindow = null;
                inversionActive = false;
            };
            
            overlayWindow.Show();
            overlayWindow.Activate();
            overlayWindow.Focus();
        }

        private bool PositionOnTargetMonitor()
        {
            // Debug: Show all available screens
            var allScreens = WinForms.Screen.AllScreens;
            for (int i = 0; i < allScreens.Length; i++)
            {
                var s = allScreens[i];
            }
            
            var targetScreen = allScreens.FirstOrDefault(s => 
                s.Bounds.X == monitorRect.X && s.Bounds.Y == monitorRect.Y &&
                s.Bounds.Width == monitorRect.Width && s.Bounds.Height == monitorRect.Height);
            
            if (targetScreen == null)
            {
                targetScreen = WinForms.Screen.PrimaryScreen ?? allScreens.First();
            }

            // Get DPI scale factor for this monitor
            var dpiScale = GetDpiScaleForScreen(targetScreen);
            
            // Calculate position in WPF units (device-independent pixels)
            var leftPosition = monitorRect.X / dpiScale.DpiScaleX;
            var topPosition = monitorRect.Y / dpiScale.DpiScaleY;
            var width = monitorRect.Width / dpiScale.DpiScaleX;
            var height = monitorRect.Height / dpiScale.DpiScaleY;
            
            // Set window position and size
            if (overlayWindow != null)
            {
                overlayWindow.Left = leftPosition;
                overlayWindow.Top = topPosition;
                overlayWindow.Width = width;
                overlayWindow.Height = height;
            }
            
            return targetScreen != null;
        }

        private DpiScale GetDpiScaleForScreen(WinForms.Screen screen)
        {
            try
            {
                // Get DPI for the specific monitor
                var point = new System.Drawing.Point(screen.Bounds.Left + 1, screen.Bounds.Top + 1);
                var monitor = MonitorFromPoint(point, MONITOR_DEFAULTTONEAREST);
                
                GetDpiForMonitor(monitor, DpiType.Effective, out uint dpiX, out uint dpiY);
                
                // Standard DPI is 96
                var scaleX = dpiX / 96.0;
                var scaleY = dpiY / 96.0;
                
                return new DpiScale(scaleX, scaleY);
            }
            catch
            {
                // Fallback to system DPI if monitor-specific DPI fails
                var source = PresentationSource.FromVisual(Application.Current.MainWindow);
                if (source?.CompositionTarget != null)
                {
                    return new DpiScale(
                        source.CompositionTarget.TransformToDevice.M11,
                        source.CompositionTarget.TransformToDevice.M22
                    );
                }
                
                // Final fallback
                return new DpiScale(1.0, 1.0);
            }
        }
        
        private double GetDpiScale()
        {
            // Get the DPI scale factor for the current system
            var dpiScale = 1.0;
            var source = PresentationSource.FromVisual(Application.Current.MainWindow);
            if (source?.CompositionTarget != null)
            {
                dpiScale = source.CompositionTarget.TransformToDevice.M11;
            }
            return dpiScale;
        }
        
        private double GetMonitorDpiScale(Rectangle monitorBounds)
        {
            // For now, assume the second monitor has 1.0 DPI scale
            // This is a simplification - proper per-monitor DPI detection would require more complex Windows API calls
            if (monitorBounds.X > 0)
            {
                return 1.0; // Second monitor typically has 1.0 scale
            }
            else
            {
                return GetDpiScale(); // Primary monitor uses system DPI
            }
        }

        private Bitmap CaptureScreen()
        {
            var bitmap = new Bitmap(monitorRect.Width, monitorRect.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(monitorRect.X, monitorRect.Y, 0, 0, 
                    new System.Drawing.Size(monitorRect.Width, monitorRect.Height), 
                    CopyPixelOperation.SourceCopy);
            }
            
            return bitmap;
        }

        private unsafe Bitmap InvertColors(Bitmap original)
        {
            var result = new Bitmap(original.Width, original.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            
            var originalData = original.LockBits(new Rectangle(0, 0, original.Width, original.Height),
                ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            
            var resultData = result.LockBits(new Rectangle(0, 0, result.Width, result.Height),
                ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

            int bytesPerPixel = 4;
            int heightInPixels = originalData.Height;
            int widthInBytes = originalData.Width * bytesPerPixel;
            
            byte* originalPtr = (byte*)originalData.Scan0;
            byte* resultPtr = (byte*)resultData.Scan0;

            for (int y = 0; y < heightInPixels; y++)
            {
                byte* originalRow = originalPtr + (y * originalData.Stride);
                byte* resultRow = resultPtr + (y * resultData.Stride);
                
                for (int x = 0; x < widthInBytes; x += bytesPerPixel)
                {
                    resultRow[x] = (byte)(255 - originalRow[x]);       // Blue
                    resultRow[x + 1] = (byte)(255 - originalRow[x + 1]); // Green
                    resultRow[x + 2] = (byte)(255 - originalRow[x + 2]); // Red
                    resultRow[x + 3] = originalRow[x + 3];              // Alpha
                }
            }

            original.UnlockBits(originalData);
            result.UnlockBits(resultData);
            
            return result;
        }

        private unsafe Bitmap MaskVideoRegions(Bitmap image, HashSet<VideoWindowDetector.RECT> videoWindows)
        {
            if (videoWindows.Count == 0)
                return image;

            var imageData = image.LockBits(new Rectangle(0, 0, image.Width, image.Height),
                ImageLockMode.ReadWrite, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

            byte* imagePtr = (byte*)imageData.Scan0;
            int bytesPerPixel = 4;

            foreach (var videoRect in videoWindows)
            {
                int relX = Math.Max(0, videoRect.Left - monitorRect.X);
                int relY = Math.Max(0, videoRect.Top - monitorRect.Y);
                int relRight = Math.Min(monitorRect.Width, videoRect.Right - monitorRect.X);
                int relBottom = Math.Min(monitorRect.Height, videoRect.Bottom - monitorRect.Y);

                if (relX < relRight && relY < relBottom)
                {
                    for (int y = relY; y < relBottom; y++)
                    {
                        for (int x = relX; x < relRight; x++)
                        {
                            int pixelIndex = (y * imageData.Stride) + (x * bytesPerPixel);
                            imagePtr[pixelIndex + 3] = 0; // Set alpha to 0 (transparent)
                        }
                    }
                }
            }

            image.UnlockBits(imageData);
            return image;
        }

        private BitmapSource ConvertToBitmapSource(Bitmap bitmap)
        {
            var bitmapData = bitmap.LockBits(
                new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                ImageLockMode.ReadOnly, bitmap.PixelFormat);

            var bitmapSource = BitmapSource.Create(
                bitmapData.Width, bitmapData.Height,
                96, 96,
                PixelFormats.Bgra32,
                null,
                bitmapData.Scan0,
                bitmapData.Stride * bitmapData.Height,
                bitmapData.Stride);

            bitmap.UnlockBits(bitmapData);
            return bitmapSource;
        }

        private async Task CaptureAndInvertLoop(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // Simple delay - overlay window just displays text
                    await Task.Delay(100, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error in capture loop: {ex.Message}");
                    break;
                }
            }
        }

        public void SetInversionActive(bool active)
        {
            inversionActive = active;
            
            if (!active && overlayWindow != null)
            {
                // Hide overlay window when inversion is deactivated
                Application.Current.Dispatcher.Invoke(() =>
                {
                    overlayWindow.Hide();
                });
            }
            else if (active && overlayWindow != null)
            {
                // Show overlay window if it already exists
                Application.Current.Dispatcher.Invoke(() =>
                {
                    overlayWindow.Show();
                });
            }
        }

        public void Start()
        {
            if (!running)
            {
                running = true;
                // Don't create overlay window here - create it only when inversion is activated
                
                cancellationTokenSource = new CancellationTokenSource();
                Task.Run(() => CaptureAndInvertLoop(cancellationTokenSource.Token));
            }
        }

        public void Stop()
        {
            if (running)
            {
                running = false;
                cancellationTokenSource?.Cancel();
                cancellationTokenSource?.Dispose();
                cancellationTokenSource = null;
                
                Application.Current.Dispatcher.Invoke(() =>
                {
                    overlayWindow?.Close();
                    overlayWindow = null;
                });
            }
        }
    }
}