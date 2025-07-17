using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace ColorInverter
{
    public class VideoWindowDetector
    {
        private static readonly string[] CommonVideoClasses = {
            "MediaPlayerClassicW",
            "VLC DirectX video output",
            "Chrome_WidgetWin_1",
            "MozillaWindowClass",
            "ApplicationFrameWindow",
            "Windows.UI.Core.CoreWindow"
        };

        private static readonly string[] VideoIndicators = {
            "youtube", "netflix", "hulu", "video", "player", "vlc", "media"
        };

        private HashSet<RECT> videoWindows = new();
        private DateTime lastUpdate = DateTime.MinValue;
        private readonly TimeSpan updateInterval = TimeSpan.FromSeconds(1);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;

            public override bool Equals(object? obj)
            {
                if (obj is RECT rect)
                {
                    return Left == rect.Left && Top == rect.Top && Right == rect.Right && Bottom == rect.Bottom;
                }
                return false;
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(Left, Top, Right, Bottom);
            }
        }

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        private bool IsVideoWindow(IntPtr hWnd)
        {
            try
            {
                var className = new StringBuilder(256);
                GetClassName(hWnd, className, className.Capacity);
                var classNameStr = className.ToString();

                var windowText = new StringBuilder(256);
                GetWindowText(hWnd, windowText, windowText.Capacity);
                var windowTextStr = windowText.ToString().ToLower();

                foreach (var videoClass in CommonVideoClasses)
                {
                    if (classNameStr.Contains(videoClass))
                        return true;
                }

                foreach (var indicator in VideoIndicators)
                {
                    if (windowTextStr.Contains(indicator))
                        return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        public HashSet<RECT> GetVideoWindows()
        {
            var currentTime = DateTime.Now;
            if (currentTime - lastUpdate < updateInterval)
                return videoWindows;

            videoWindows.Clear();

            EnumWindows((hWnd, lParam) =>
            {
                if (IsWindowVisible(hWnd) && IsVideoWindow(hWnd))
                {
                    if (GetWindowRect(hWnd, out RECT rect))
                    {
                        videoWindows.Add(rect);
                    }
                }
                return true;
            }, IntPtr.Zero);

            lastUpdate = currentTime;
            return videoWindows;
        }
    }
}