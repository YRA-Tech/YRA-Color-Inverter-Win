using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Threading;
 
namespace ColorInverter
{
    public partial class MainWindow : Window
    {
        private MonitorManager monitorManager;
        private VideoWindowDetector videoDetector;
        private HotkeyDetector hotkeyDetector;
        private ColorInverterCore? inverterCore;
        private bool inversionEnabled = false;
        private Window? simpleOverlay;
        private System.Windows.Controls.Image? overlayImage;
        private System.Threading.Timer? captureTimer;

        public MainWindow()
        {
            InitializeComponent();
            
            monitorManager = new MonitorManager();
            videoDetector = new VideoWindowDetector();
            hotkeyDetector = new HotkeyDetector();
            
            RefreshMonitors();
        }

        private void RefreshMonitors()
        {
            monitorManager.RefreshMonitors();
            MonitorComboBox.ItemsSource = monitorManager.GetMonitorDisplayNames();
            
            if (MonitorComboBox.Items.Count > 0)
            {
                MonitorComboBox.SelectedIndex = 0;
            }
            
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshMonitors();
        }

        private void MonitorComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Monitor selection changed - no immediate action needed
        }

        private void ToggleInversion()
        {
            // Ensure we're on the UI thread
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(ToggleInversion);
                return;
            }

            if (MonitorComboBox.SelectedIndex < 0)
            {
                StatusLabel.Content = "❌ Please select a monitor first";
                StatusLabel.Foreground = System.Windows.Media.Brushes.Red;
                return;
            }

            var monitors = monitorManager.GetMonitors();
            if (MonitorComboBox.SelectedIndex >= monitors.Count)
            {
                StatusLabel.Content = "❌ Invalid monitor selection";
                StatusLabel.Foreground = System.Windows.Media.Brushes.Red;
                return;
            }

            var selectedMonitor = monitors[MonitorComboBox.SelectedIndex];
            

            if (inverterCore != null)
            {
                inversionEnabled = !inversionEnabled;
                
                if (inversionEnabled)
                {
                    // Stop the current core and create a new one with updated monitor bounds
                    inverterCore.Stop();
                    inverterCore = new ColorInverterCore(selectedMonitor.PhysicalBounds, videoDetector);
                    inverterCore.Start();
                    
                    // Create simple 400x400 overlay window
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        CreateSimpleOverlay(selectedMonitor);
                    });
                    
                    StatusLabel.Content = $"🟢 INVERTING {selectedMonitor.Name} - Press Ctrl+Shift+I to toggle";
                    StatusLabel.Foreground = System.Windows.Media.Brushes.Green;
                }
                else
                {
                    // Hide simple overlay
                    if (simpleOverlay != null)
                    {
                        StopScreenCapture();
                        try
                        {
                            simpleOverlay.Close();
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error closing overlay: {ex.Message}");
                        }
                        simpleOverlay = null;
                        overlayImage = null;
                    }
                    
                    StatusLabel.Content = "🔴 Detection active - Press Ctrl+Shift+I to toggle";
                    StatusLabel.Foreground = System.Windows.Media.Brushes.Red;
                }
            }
            else
            {
                StatusLabel.Content = "❌ Error: inverterCore is null";
                StatusLabel.Foreground = System.Windows.Media.Brushes.Red;
            }
        }

        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Create a placeholder inverter core - actual monitor will be selected when hotkey is pressed
                var monitors = monitorManager.GetMonitors();
                if (monitors.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine("No monitors detected");
                    return;
                }

                // Use first monitor as placeholder - will be updated when hotkey is pressed
                inverterCore = new ColorInverterCore(monitors[0].PhysicalBounds, videoDetector);
                inverterCore.Start();

                // Setup hotkey detection
                hotkeyDetector.RemoveCallback(ToggleInversion); // Remove any existing callback
                hotkeyDetector.AddCallback(ToggleInversion);
                hotkeyDetector.Start();

                StartButton.IsEnabled = false;
                StopButton.IsEnabled = true;
                StatusLabel.Content = "🔴 Detection active - Press Ctrl+Shift+I to toggle";
                StatusLabel.Foreground = System.Windows.Media.Brushes.Red;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to start detection: {ex.Message}");
            }
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            hotkeyDetector.Stop();
            hotkeyDetector.RemoveCallback(ToggleInversion);
            
            if (inverterCore != null)
            {
                inverterCore.Stop();
                inverterCore = null;
            }

            // Close overlay if it exists
            if (simpleOverlay != null)
            {
                StopScreenCapture();
                simpleOverlay.Close();
                simpleOverlay = null;
                overlayImage = null;
            }

            inversionEnabled = false;
            StartButton.IsEnabled = true;
            StopButton.IsEnabled = false;
            StatusLabel.Content = "Ready";
        }


        private void CreateSimpleOverlay(MonitorManager.MonitorData monitor)
        {
            // Close existing overlay if any
            if (simpleOverlay != null)
            {
                StopScreenCapture();
                simpleOverlay.Close();
                simpleOverlay = null;
                overlayImage = null;
            }
            
            // Use DPI scale factor from MonitorData (already calculated correctly)
            var dpiScale = monitor.DpiScale;
            
            // Calculate position in WPF units (device-independent pixels)
            // Monitor bounds are in physical pixels, convert to logical pixels
            var leftPosition = monitor.PhysicalBounds.X / dpiScale;
            var topPosition = monitor.PhysicalBounds.Y / dpiScale;
            
            
            // Create full-screen overlay for selected monitor
            var overlayWidth = monitor.PhysicalBounds.Width / dpiScale;
            var overlayHeight = monitor.PhysicalBounds.Height / dpiScale;
            
            simpleOverlay = new Window
            {
                Title = "Screen Capture Overlay",
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                AllowsTransparency = true,
                Background = System.Windows.Media.Brushes.Transparent, // Transparent background
                Topmost = true,
                ShowInTaskbar = false, // Hide from taskbar
                WindowState = WindowState.Normal,
                IsHitTestVisible = false, // Pass through all mouse and keyboard events
                Width = overlayWidth,   // Full monitor width
                Height = overlayHeight, // Full monitor height
                Left = leftPosition,    // Monitor X position
                Top = topPosition       // Monitor Y position
            };
            
            
            // Create image control for screen capture display
            overlayImage = new System.Windows.Controls.Image
            {
                Stretch = System.Windows.Media.Stretch.Fill,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                VerticalAlignment = System.Windows.VerticalAlignment.Stretch
            };
            
            simpleOverlay.Content = overlayImage;
            simpleOverlay.Show();
            
            // Don't activate or bring into view to avoid system notifications
            
            
            // Start screen capture for the overlay
            StartScreenCapture(monitor);
        }

        private void StartScreenCapture(MonitorManager.MonitorData monitor)
        {
            // Stop any existing timer
            StopScreenCapture();
            
            
            
            // Start timer to capture screen once per second
            captureTimer = new System.Threading.Timer(
                callback: _ => CaptureScreen(monitor),
                state: null,
                dueTime: 0,
                period: 1000 // 1 second
            );
        }
        
        private void StopScreenCapture()
        {
            captureTimer?.Dispose();
            captureTimer = null;
        }
        
        private void CaptureScreen(MonitorManager.MonitorData monitor)
        {
            try
            {
                // Hide overlay window before capture to prevent feedback loop
                bool overlayWasVisible = false;
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    if (simpleOverlay != null && simpleOverlay.IsVisible)
                    {
                        overlayWasVisible = true;
                        simpleOverlay.Hide();
                    }
                });

                // Calculate physical coordinates for full screen capture
                var physicalX = monitor.PhysicalBounds.X;      // Monitor X position
                var physicalY = monitor.PhysicalBounds.Y;      // Monitor Y position
                var physicalWidth = monitor.PhysicalBounds.Width;   // Full monitor width
                var physicalHeight = monitor.PhysicalBounds.Height; // Full monitor height
                
                
                // Capture screen
                using (var bitmap = new Bitmap(physicalWidth, physicalHeight, PixelFormat.Format32bppArgb))
                {
                    using (var graphics = Graphics.FromImage(bitmap))
                    {
                        try
                        {
                            graphics.CopyFromScreen(physicalX, physicalY, 0, 0, 
                                new System.Drawing.Size(physicalWidth, physicalHeight), 
                                CopyPixelOperation.SourceCopy);
                        }
                        catch (Exception)
                        {
                            graphics.Clear(Color.Red);
                            using (var brush = new SolidBrush(Color.White))
                            using (var font = new Font("Arial", 16))
                            {
                                graphics.DrawString("CAPTURE FAILED", font, brush, 10, 10);
                            }
                        }
                    }
                    
                    // Apply color inversion: invert all RGB values
                    for (int y = 0; y < bitmap.Height; y++)
                    {
                        for (int x = 0; x < bitmap.Width; x++)
                        {
                            Color pixel = bitmap.GetPixel(x, y);
                            Color newPixel = Color.FromArgb(pixel.A, 255 - pixel.R, 255 - pixel.G, 255 - pixel.B);
                            bitmap.SetPixel(x, y, newPixel);
                        }
                    }
                    
                    // Copy bitmap data on background thread, then create BitmapSource on UI thread
                    var bitmapData = bitmap.LockBits(
                        new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height),
                        ImageLockMode.ReadOnly, bitmap.PixelFormat);
                    
                    var pixelData = new byte[bitmapData.Stride * bitmapData.Height];
                    System.Runtime.InteropServices.Marshal.Copy(bitmapData.Scan0, pixelData, 0, pixelData.Length);
                    var width = bitmapData.Width;
                    var height = bitmapData.Height;
                    var stride = bitmapData.Stride;
                    
                    bitmap.UnlockBits(bitmapData);
                    
                    // Capture overlayWasVisible for use in UI callback
                    var wasVisible = overlayWasVisible;
                    
                    // Update UI on main thread with copied data
                    System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
                    {
                        try
                        {
                            if (overlayImage != null && simpleOverlay != null)
                            {
                                // Create BitmapSource on UI thread using copied data
                                var bitmapSource = BitmapSource.Create(
                                    width, height,
                                    96, 96,
                                    System.Windows.Media.PixelFormats.Bgra32,
                                    null,
                                    pixelData,
                                    stride);
                                
                                // Freeze the bitmap to make it cross-thread safe
                                bitmapSource.Freeze();
                                    
                                overlayImage.Source = bitmapSource;
                                
                                // Show overlay window again if it was hidden for capture
                                if (wasVisible && !simpleOverlay.IsVisible)
                                {
                                    simpleOverlay.Show();
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error updating overlay image: {ex.Message}");
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Screen capture error: {ex.Message}");
            }
        }
        
        private BitmapSource ConvertToBitmapSource(Bitmap bitmap)
        {
            // Use more efficient GDI+ approach similar to Opus's suggestion
            var hBitmap = bitmap.GetHbitmap();
            try
            {
                return Imaging.CreateBitmapSourceFromHBitmap(
                    hBitmap,
                    IntPtr.Zero,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
            }
            finally
            {
                DeleteObject(hBitmap);
            }
        }

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        private void ApplyColorInversion(Bitmap bitmap, int captureX, int captureY)
        {
            // Get video windows that might be in the captured area
            var videoWindows = GetVideoWindowsInCaptureArea(captureX, captureY, bitmap.Width, bitmap.Height);
            
            
            // Lock bitmap for direct pixel manipulation
            var bitmapData = bitmap.LockBits(
                new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height),
                ImageLockMode.ReadWrite, bitmap.PixelFormat);

            unsafe
            {
                byte* ptr = (byte*)bitmapData.Scan0;
                int bytesPerPixel = 4; // BGRA32
                int stride = bitmapData.Stride;

                for (int y = 0; y < bitmap.Height; y++)
                {
                    for (int x = 0; x < bitmap.Width; x++)
                    {
                        // Calculate screen coordinates for this pixel
                        int screenX = captureX + x;
                        int screenY = captureY + y;
                        
                        // Check if this pixel is in a video window
                        bool isInVideoWindow = IsPixelInVideoWindow(screenX, screenY, videoWindows);
                        
                        if (!isInVideoWindow)
                        {
                            // Get pixel offset
                            int pixelOffset = y * stride + x * bytesPerPixel;
                            
                            // Get BGRA values
                            byte blue = ptr[pixelOffset];
                            byte green = ptr[pixelOffset + 1];
                            byte red = ptr[pixelOffset + 2];
                            byte alpha = ptr[pixelOffset + 3];
                            
                            // Set red to 255, leave green and blue unchanged
                            ptr[pixelOffset] = blue;      // B - unchanged
                            ptr[pixelOffset + 1] = green; // G - unchanged
                            ptr[pixelOffset + 2] = 255;   // R - set to maximum
                            // Keep alpha unchanged
                        }
                    }
                }
            }

            bitmap.UnlockBits(bitmapData);
        }

        private List<System.Drawing.Rectangle> GetVideoWindowsInCaptureArea(int captureX, int captureY, int captureWidth, int captureHeight)
        {
            var videoWindows = new List<System.Drawing.Rectangle>();
            
            // Get current video windows from VideoWindowDetector
            var detectedVideoWindows = videoDetector.GetVideoWindows();
            
            var captureRect = new System.Drawing.Rectangle(captureX, captureY, captureWidth, captureHeight);
            
            // Convert RECT to Rectangle and check if they intersect with capture area
            foreach (var videoWindow in detectedVideoWindows)
            {
                var windowRect = new System.Drawing.Rectangle( 
                    videoWindow.Left, 
                    videoWindow.Top, 
                    videoWindow.Right - videoWindow.Left, 
                    videoWindow.Bottom - videoWindow.Top);
                
                // Check if video window intersects with capture area
                if (captureRect.IntersectsWith(windowRect))
                {
                    videoWindows.Add(windowRect);
                }
            }
            
            return videoWindows;
        }
        private bool IsPixelInVideoWindow(int screenX, int screenY, List<System.Drawing.Rectangle> videoWindows)
        {
            foreach (var window in videoWindows)
            {
                if (window.Contains(screenX, screenY))
                {
                    return true;
                }
            }
            return false;
        }

        private (double DpiScaleX, double DpiScaleY) GetDpiScale(Screen screen)
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
                
                return (scaleX, scaleY);
            }
            catch
            {
                // Fallback to system DPI if monitor-specific DPI fails
                var source = PresentationSource.FromVisual(this);
                if (source?.CompositionTarget != null)
                {
                    return (
                        source.CompositionTarget.TransformToDevice.M11,
                        source.CompositionTarget.TransformToDevice.M22
                    );
                }
                
                // Final fallback
                return (1.0, 1.0);
            }
        }

        // Win32 API declarations for DPI detection
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(System.Drawing.Point pt, uint dwFlags);

        [System.Runtime.InteropServices.DllImport("shcore.dll")]
        private static extern int GetDpiForMonitor(IntPtr hmonitor, DpiType dpiType, out uint dpiX, out uint dpiY);

        private const uint MONITOR_DEFAULTTONEAREST = 2;

        private enum DpiType
        {
            Effective = 0,
            Angular = 1,
            Raw = 2,
        }

        protected override void OnClosed(EventArgs e)
        {
            hotkeyDetector?.Stop();
            inverterCore?.Stop();
            StopScreenCapture();
            simpleOverlay?.Close();
            base.OnClosed(e);
        }
    }
}