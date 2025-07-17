using System;
using System.Runtime.InteropServices;
using System.Collections.Generic;

namespace ColorInverter
{
    public class HotkeyDetector
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int VK_CONTROL = 0x11;
        private const int VK_SHIFT = 0x10;
        private const int VK_I = 0x49;

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        private static extern short GetKeyState(int nVirtKey);

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        private LowLevelKeyboardProc _proc;
        private IntPtr _hookID = IntPtr.Zero;
        private readonly List<Action> callbacks = new();
        private bool running;

        public HotkeyDetector()
        {
            _proc = HookCallback;
            // Keep a strong reference to prevent GC
            GC.KeepAlive(_proc);
        }

        public void AddCallback(Action callback)
        {
            callbacks.Add(callback);
        }

        public void RemoveCallback(Action callback)
        {
            callbacks.Remove(callback);
        }

        private bool IsKeyPressed(int vkCode)
        {
            return (GetKeyState(vkCode) & 0x8000) != 0;
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && wParam == (IntPtr)WM_KEYDOWN)
            {
                var hookStruct = (KBDLLHOOKSTRUCT?)Marshal.PtrToStructure(lParam, typeof(KBDLLHOOKSTRUCT));
                
                if (hookStruct.HasValue)
                {
                    // Check if Ctrl+Shift+I is pressed
                    if (hookStruct.Value.vkCode == VK_I && IsKeyPressed(VK_CONTROL) && IsKeyPressed(VK_SHIFT))
                    {
                        foreach (var callback in callbacks)
                        {
                            try
                            {
                                callback();
                            }
                            catch
                            {
                                // Ignore callback errors
                            }
                        }
                    }
                }
            }

            return CallNextHookEx(_hookID, nCode, wParam, lParam);
        }

        public void Start()
        {
            if (!running)
            {
                running = true;
                _hookID = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null!), 0);
                if (_hookID == IntPtr.Zero)
                {
                    int error = Marshal.GetLastWin32Error();
                    throw new Exception($"Failed to install keyboard hook. Error code: {error}");
                }
            }
        }

        public void Stop()
        {
            if (running)
            {
                running = false;
                if (_hookID != IntPtr.Zero)
                {
                    UnhookWindowsHookEx(_hookID);
                    _hookID = IntPtr.Zero;
                }
            }
        }
    }
}