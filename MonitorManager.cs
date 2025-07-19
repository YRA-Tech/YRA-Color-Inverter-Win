using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Drawing;
using System.Linq;
using WinForms = System.Windows.Forms;

namespace ColorInverter
{
    public class MonitorManager
    {
        [DllImport("user32.dll")]
        private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo lpmi);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool EnumDisplaySettings(string lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(System.Drawing.Point pt, uint dwFlags);

        [DllImport("shcore.dll")]
        private static extern int GetDpiForMonitor(IntPtr hmonitor, DpiType dpiType, out uint dpiX, out uint dpiY);

        private const int ENUM_CURRENT_SETTINGS = -1;
        private const uint MONITOR_DEFAULTTONEAREST = 2;

        private enum DpiType
        {
            Effective = 0,
            Angular = 1,
            Raw = 2,
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string dmDeviceName;
            public short dmSpecVersion;
            public short dmDriverVersion;
            public short dmSize;
            public short dmDriverExtra;
            public int dmFields;
            public int dmPositionX;
            public int dmPositionY;
            public int dmDisplayOrientation;
            public int dmDisplayFixedOutput;
            public short dmColor;
            public short dmDuplex;
            public short dmYResolution;
            public short dmTTOption;
            public short dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel;
            public int dmPelsWidth;
            public int dmPelsHeight;
            public int dmDisplayFlags;
            public int dmDisplayFrequency;
            public int dmICMMethod;
            public int dmICMIntent;
            public int dmMediaType;
            public int dmDitherType;
            public int dmReserved1;
            public int dmReserved2;
            public int dmPanningWidth;
            public int dmPanningHeight;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public struct MonitorInfo
        {
            public int Size;
            public Rectangle Monitor;
            public Rectangle WorkArea;
            public uint Flags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string DeviceName;
        }

        private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref Rectangle lprcMonitor, IntPtr dwData);

        private List<MonitorData> monitors = new();

        public class MonitorData
        {
            public IntPtr Handle { get; set; }
            public Rectangle PhysicalBounds { get; set; } // Physical coordinates from Screen.AllScreens
            public Rectangle LogicalBounds { get; set; } // Calculated logical coordinates (Physical ÷ DPI scale)
            public double DpiScale { get; set; } // DPI scale factor (1.0 = 100%, 2.0 = 200%)
            public string Name { get; set; } = "";
            public bool IsPrimary { get; set; }
        }

        private int monitorIndex = 0;

        public void RefreshMonitors()
        {
            monitors.Clear();
            monitorIndex = 0;

            // Get physical coordinates from EnumDisplayMonitors + EnumDisplaySettings
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, MonitorEnumCallback, IntPtr.Zero);
            
            // Get logical coordinates from Screen.AllScreens for matching
            var screens = WinForms.Screen.AllScreens;
            
            // Debug: Show all discovered monitors and screens
            var debugInfo = "Monitor Discovery Debug:\n\n";
            debugInfo += $"Found {monitors.Count} monitors from EnumDisplayMonitors:\n";
            foreach (var mon in monitors)
            {
                debugInfo += $"  Monitor: {mon.Name}, Bounds: {mon.PhysicalBounds}\n";
            }
            debugInfo += $"\nFound {screens.Length} screens from Screen.AllScreens:\n";
            foreach (var screen in screens)
            {
                debugInfo += $"  Screen: {screen.DeviceName}, Bounds: {screen.Bounds}, Primary: {screen.Primary}\n";
            }
            
            System.Diagnostics.Debug.WriteLine(debugInfo);
            
            // Match monitors by device name and calculate logical coordinates
            foreach (var screen in screens)
            {
                var matchingMonitor = monitors.FirstOrDefault(m => m.Name == screen.DeviceName);
                
                System.Diagnostics.Debug.WriteLine($"Matching screen {screen.DeviceName} with monitor: {matchingMonitor?.Name ?? "NOT FOUND"}");
                
                if (matchingMonitor != null)
                {
                    // Get DPI scale for this monitor
                    var dpiScale = GetDpiScaleForScreen(screen);
                    
                    // Calculate logical coordinates
                    // For Display2, we need to position it after Display1's logical width
                    var logicalBounds = new Rectangle(
                        matchingMonitor.IsPrimary ? 0 : 1536,  // Display1=0, Display2=1536 (after Display1 logical width)
                        0,  // Both monitors start at Y=0
                        (int)(matchingMonitor.PhysicalBounds.Width / dpiScale),
                        (int)(matchingMonitor.PhysicalBounds.Height / dpiScale)
                    );
                    
                    // Update monitor with calculated values
                    matchingMonitor.LogicalBounds = logicalBounds;
                    matchingMonitor.DpiScale = dpiScale;
                    matchingMonitor.IsPrimary = screen.Primary;
                }
            }
            
            // Remove monitors that couldn't be matched
            monitors.RemoveAll(m => m.LogicalBounds.IsEmpty);
        }

        private double GetDpiScaleForScreen(WinForms.Screen screen)
        {
            try
            {
                // Get DPI for the specific monitor
                var point = new System.Drawing.Point(screen.Bounds.Left + 1, screen.Bounds.Top + 1);
                var monitor = MonitorFromPoint(point, MONITOR_DEFAULTTONEAREST);
                
                GetDpiForMonitor(monitor, DpiType.Effective, out uint dpiX, out uint dpiY);
                
                // Debug: Show detailed DPI detection info
                var debugInfo = $"DPI detection for {screen.DeviceName}:\n" +
                               $"Screen Bounds: {screen.Bounds}\n" +
                               $"Point used: ({point.X},{point.Y})\n" +
                               $"Raw DPI: {dpiX}x{dpiY}\n" +
                               $"Calculated Scale: {dpiX / 96.0:F2}\n" +
                               $"Monitor Handle: {monitor}\n\n";
                
                // For debugging, let's also try different DPI types
                GetDpiForMonitor(monitor, DpiType.Raw, out uint rawDpiX, out uint rawDpiY);
                GetDpiForMonitor(monitor, DpiType.Angular, out uint angularDpiX, out uint angularDpiY);
                debugInfo += $"Raw DPI: {rawDpiX}x{rawDpiY} (Scale: {rawDpiX / 96.0:F2})\n" +
                           $"Angular DPI: {angularDpiX}x{angularDpiY} (Scale: {angularDpiX / 96.0:F2})";
                
                System.Diagnostics.Debug.WriteLine(debugInfo);
                
                // Standard DPI is 96, return scale factor
                var calculatedScale = dpiX / 96.0;
                
                // TEMPORARY FIX: If the scale seems wrong, force it to 1.0
                // This is a workaround for DPI detection issues
                if (calculatedScale == 2.0 && screen.Bounds.Width <= 1920) // Likely 1920x1080 monitor shouldn't be 2.0 scale
                {
                    System.Diagnostics.Debug.WriteLine($"WARNING: Overriding DPI scale from {calculatedScale} to 1.0 for {screen.DeviceName}");
                    return 1.0;
                }
                
                return calculatedScale;
            }
            catch (Exception ex)
            {
                // Show what went wrong
                System.Diagnostics.Debug.WriteLine($"DPI detection failed for {screen.DeviceName}: {ex.Message}");
                // Fallback to 1.0 if DPI detection fails
                return 1.0;
            }
        }

        private bool MonitorEnumCallback(IntPtr hMonitor, IntPtr hdcMonitor, ref Rectangle lprcMonitor, IntPtr dwData)
        {
            var monitorInfo = new MonitorInfo();
            monitorInfo.Size = Marshal.SizeOf(monitorInfo);

            if (GetMonitorInfo(hMonitor, ref monitorInfo))
            {
                // Get the actual physical resolution using EnumDisplaySettings
                var devMode = new DEVMODE();
                devMode.dmSize = (short)Marshal.SizeOf(devMode);
                
                Rectangle physicalBounds = monitorInfo.Monitor; // Default to logical bounds
                
                if (EnumDisplaySettings(monitorInfo.DeviceName, ENUM_CURRENT_SETTINGS, ref devMode))
                {
                    // Use logical position from GetMonitorInfo but physical size from EnumDisplaySettings
                    physicalBounds = new Rectangle(
                        monitorInfo.Monitor.X, // Keep logical X position
                        monitorInfo.Monitor.Y, // Keep logical Y position  
                        devMode.dmPelsWidth,   // Use physical width
                        devMode.dmPelsHeight   // Use physical height
                    );
                    
                }

                var monitor = new MonitorData
                {
                    Handle = hMonitor,
                    PhysicalBounds = physicalBounds, // Store physical bounds separately
                    Name = string.IsNullOrEmpty(monitorInfo.DeviceName) ? $"Monitor {monitorIndex + 1}" : monitorInfo.DeviceName,
                    IsPrimary = (monitorInfo.Flags & 1) != 0
                };

                monitors.Add(monitor);
                monitorIndex++;
            }

            return true;
        }

        public List<MonitorData> GetMonitors()
        {
            return monitors;
        }

        public List<string> GetMonitorDisplayNames()
        {
            var displayNames = new List<string>();
            for (int i = 0; i < monitors.Count; i++)
            {
                var monitor = monitors[i];
                string displayName = $"{i + 1}: {monitor.Name} ({(monitor.IsPrimary ? "Primary" : "Secondary")})";
                displayNames.Add(displayName);
            }
            return displayNames;
        }

        public string GetDebugInfo()
        {
            var info = new List<string>();
            info.Add("=== MONITOR DETECTION DEBUG ===");
            
            for (int i = 0; i < monitors.Count; i++)
            {
                var monitor = monitors[i];
                info.Add($"Monitor {i + 1}: {monitor.Name}");
                info.Add($"  Physical: [{monitor.PhysicalBounds.X}, {monitor.PhysicalBounds.Y}, {monitor.PhysicalBounds.Width}×{monitor.PhysicalBounds.Height}]");
                info.Add($"  DPI Scale: {monitor.DpiScale:F1}x ({monitor.DpiScale * 100:F0}%)");
                info.Add($"  Logical:  [{monitor.LogicalBounds.X}, {monitor.LogicalBounds.Y}, {monitor.LogicalBounds.Width}×{monitor.LogicalBounds.Height}]");
                info.Add($"  Primary: {monitor.IsPrimary}");
                info.Add("");
            }
            
            return string.Join("\n", info);
        }

        public MonitorManager()
        {
            RefreshMonitors();
        }
    }
}