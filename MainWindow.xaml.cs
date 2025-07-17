using System;
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
            
            // Show debug info popup on startup
            System.Windows.MessageBox.Show(monitorManager.GetDebugInfo(), "Monitor Detection Debug", MessageBoxButton.OK, MessageBoxImage.Information);
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
                    System.Windows.MessageBox.Show("No monitors detected", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
                System.Windows.MessageBox.Show($"Failed to start detection: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
            
            // Create 400x400 overlay at upper-left corner of selected monitor
            simpleOverlay = new Window
            {
                Title = "Screen Capture Overlay",
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                AllowsTransparency = true,
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(128, 255, 0, 0)), // Semi-transparent red for debugging
                Topmost = true,
                ShowInTaskbar = true, // Show in taskbar for debugging
                WindowState = WindowState.Normal,
                Width = 400,  // Fixed 400 logical pixels
                Height = 400, // Fixed 400 logical pixels
                Left = leftPosition,   // Upper-left X of monitor (logical coordinates)
                Top = topPosition      // Upper-left Y of monitor (logical coordinates)
            };
            
            // Debug: Show window creation info
            System.Windows.MessageBox.Show($"Creating overlay at:\nLeft: {simpleOverlay.Left}\nTop: {simpleOverlay.Top}\nSize: {simpleOverlay.Width}x{simpleOverlay.Height}\nDPI Scale: {dpiScale}x{dpiScale}\nMonitor: {monitor.Name}", 
                "Overlay Debug", MessageBoxButton.OK, MessageBoxImage.Information);
            
            // Create image control for screen capture display
            overlayImage = new System.Windows.Controls.Image
            {
                Stretch = System.Windows.Media.Stretch.Fill,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            };
            
            simpleOverlay.Content = overlayImage;
            simpleOverlay.Show();
            simpleOverlay.Activate();
            simpleOverlay.Focus();
            
            // Start screen capture for the overlay
            StartScreenCapture(monitor);
        }

        private void StartScreenCapture(MonitorManager.MonitorData monitor)
        {
            // Stop any existing timer
            StopScreenCapture();
            
            // Debug: Confirm screen capture is starting
            System.Windows.MessageBox.Show($"Starting screen capture for monitor: {monitor.Name}", 
                "Screen Capture Start", MessageBoxButton.OK, MessageBoxImage.Information);
            
            // Start timer to capture screen at 30 FPS
            captureTimer = new System.Threading.Timer(
                callback: _ => CaptureScreen(monitor),
                state: null,
                dueTime: 0,
                period: 33 // ~30 FPS
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
                // Calculate physical coordinates for screen capture
                // Capture 400x400 logical pixels from upper-left corner of monitor
                var physicalX = monitor.PhysicalBounds.X;  // Upper-left X of monitor
                var physicalY = monitor.PhysicalBounds.Y;  // Upper-left Y of monitor
                var physicalWidth = (int)(400 * monitor.DpiScale);
                var physicalHeight = (int)(400 * monitor.DpiScale);
                
                // Debug: Show capture info once at startup
                if (captureTimer != null && System.DateTime.Now.Millisecond < 100) // Only show debug occasionally
                {
                    var debugInfo = $"Screen Capture Debug:\n" +
                                   $"Monitor: {monitor.Name}\n" +
                                   $"Physical Bounds: {monitor.PhysicalBounds}\n" +
                                   $"Logical Bounds: {monitor.LogicalBounds}\n" +
                                   $"DPI Scale: {monitor.DpiScale}\n" +
                                   $"Capture coords: X={physicalX}, Y={physicalY}\n" +
                                   $"Capture size: W={physicalWidth}, H={physicalHeight}";
                    
                    System.Windows.MessageBox.Show(debugInfo, "Screen Capture Debug", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                
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
                        catch (Exception screenCaptureEx)
                        {
                            // If screen capture fails, fill with red for debugging
                            System.Windows.MessageBox.Show($"Screen capture failed: {screenCaptureEx.Message}", 
                                "Screen Capture Error", MessageBoxButton.OK, MessageBoxImage.Error);
                            graphics.Clear(Color.Red);
                            using (var brush = new SolidBrush(Color.White))
                            using (var font = new Font("Arial", 16))
                            {
                                graphics.DrawString("CAPTURE FAILED", font, brush, 10, 10);
                            }
                        }
                    }
                    
                    // Convert to WPF BitmapSource
                    var bitmapSource = ConvertToBitmapSource(bitmap);
                    
                    // Update UI on main thread
                    System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
                    {
                        try
                        {
                            if (overlayImage != null && simpleOverlay != null)
                            {
                                overlayImage.Source = bitmapSource;
                                // Only show success message once
                                if (System.DateTime.Now.Millisecond < 50)
                                {
                                    System.Windows.MessageBox.Show($"Updated overlay image: {bitmapSource.Width}x{bitmapSource.Height}", 
                                        "Image Update Success", MessageBoxButton.OK, MessageBoxImage.Information);
                                }
                            }
                            else
                            {
                                System.Windows.MessageBox.Show("overlayImage or simpleOverlay is null!", 
                                    "Update Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Windows.MessageBox.Show($"Error updating overlay image: {ex.Message}", 
                                "UI Update Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                // Show actual error details
                System.Windows.MessageBox.Show($"Screen capture error: {ex.Message}\n\nStack trace: {ex.StackTrace}", 
                    "Screen Capture Exception", MessageBoxButton.OK, MessageBoxImage.Error);
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