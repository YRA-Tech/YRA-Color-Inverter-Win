using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
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
                        simpleOverlay.Close();
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
                simpleOverlay.Close();
                StopScreenCapture();
            }
            
            // Get all screens and find the matching screen for DPI calculations
            var screens = Screen.AllScreens;
            var matchingScreen = screens.FirstOrDefault(s => 
                s.Bounds.Left == monitor.PhysicalBounds.X && 
                s.Bounds.Top == monitor.PhysicalBounds.Y);
            
            if (matchingScreen == null)
            {
                // Fallback to primary screen
                matchingScreen = Screen.PrimaryScreen ?? Screen.AllScreens.FirstOrDefault();
            }
            
            if (matchingScreen == null)
            {
                // No screens available - this shouldn't happen but handle gracefully
                return;
            }
            
            // Get DPI scale factor for this monitor
            var dpiScale = GetDpiScale(matchingScreen);
            
            // Calculate position in WPF units (device-independent pixels)
            // Monitor bounds are in physical pixels, convert to logical pixels
            var leftPosition = matchingScreen.Bounds.Left / dpiScale.DpiScaleX;
            var topPosition = matchingScreen.Bounds.Top / dpiScale.DpiScaleY;
            
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
            System.Windows.MessageBox.Show($"Creating overlay at:\nLeft: {simpleOverlay.Left}\nTop: {simpleOverlay.Top}\nSize: {simpleOverlay.Width}x{simpleOverlay.Height}\nDPI Scale: {dpiScale.DpiScaleX}x{dpiScale.DpiScaleY}", 
                "Overlay Debug", MessageBoxButton.OK, MessageBoxImage.Information);
            
            // Temporarily use text instead of image for debugging
            var textBlock = new System.Windows.Controls.TextBlock
            {
                Text = "OVERLAY WINDOW TEST",
                FontSize = 24,
                Foreground = System.Windows.Media.Brushes.White,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            };
            
            simpleOverlay.Content = textBlock;
            simpleOverlay.Show();
            simpleOverlay.Activate();
            simpleOverlay.Focus();
            
            // TODO: Start screen capture
            // StartScreenCapture(monitor);
        }

        private void StartScreenCapture(MonitorManager.MonitorData monitor)
        {
            // Stop any existing timer
            StopScreenCapture();
            
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
                // We need to capture the 400x400 logical area converted to physical pixels
                var physicalX = (int)(monitor.LogicalBounds.X * monitor.DpiScale);
                var physicalY = (int)(monitor.LogicalBounds.Y * monitor.DpiScale);
                var physicalWidth = (int)(400 * monitor.DpiScale);
                var physicalHeight = (int)(400 * monitor.DpiScale);
                
                // Capture screen
                using (var bitmap = new Bitmap(physicalWidth, physicalHeight, PixelFormat.Format32bppArgb))
                {
                    using (var graphics = Graphics.FromImage(bitmap))
                    {
                        graphics.CopyFromScreen(physicalX, physicalY, 0, 0, 
                            new System.Drawing.Size(physicalWidth, physicalHeight), 
                            CopyPixelOperation.SourceCopy);
                    }
                    
                    // Convert to WPF BitmapSource
                    var bitmapSource = ConvertToBitmapSource(bitmap);
                    
                    // Update UI on main thread
                    System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
                    {
                        if (overlayImage != null)
                        {
                            overlayImage.Source = bitmapSource;
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                // Handle any errors silently or log them
                System.Diagnostics.Debug.WriteLine($"Screen capture error: {ex.Message}");
            }
        }
        
        private BitmapSource ConvertToBitmapSource(Bitmap bitmap)
        {
            var bitmapData = bitmap.LockBits(
                new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height),
                ImageLockMode.ReadOnly, bitmap.PixelFormat);

            var bitmapSource = BitmapSource.Create(
                bitmapData.Width, bitmapData.Height,
                96, 96,
                System.Windows.Media.PixelFormats.Bgra32,
                null,
                bitmapData.Scan0,
                bitmapData.Stride * bitmapData.Height,
                bitmapData.Stride);

            bitmap.UnlockBits(bitmapData);
            return bitmapSource;
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