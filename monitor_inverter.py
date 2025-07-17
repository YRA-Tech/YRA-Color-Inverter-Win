#!/usr/bin/env python3
"""
Monitor Color Inverter
Inverts colors on a selected monitor while excluding video windows.
"""

import cv2
import numpy as np
import tkinter as tk
from tkinter import ttk, messagebox
import threading
import time
import sys
import ctypes
from ctypes import wintypes
import win32gui
import win32con
import win32api
from mss import mss
from PIL import Image, ImageTk


class WindowsKeyDetector:
    """Detects Windows key press and hold events."""
    
    VK_LWIN = 0x5B  # Left Windows key
    VK_RWIN = 0x5C  # Right Windows key
    
    def __init__(self, hold_duration=2.0):
        self.hold_duration = hold_duration
        self.key_pressed = False
        self.press_start_time = 0
        self.running = False
        self.detection_thread = None
        self.callbacks = []
        
    def add_callback(self, callback):
        """Add callback function to be called when Windows key is held."""
        self.callbacks.append(callback)
        
    def remove_callback(self, callback):
        """Remove callback function."""
        if callback in self.callbacks:
            self.callbacks.remove(callback)
            
    def is_key_pressed(self, vk_code):
        """Check if a virtual key is currently pressed."""
        return ctypes.windll.user32.GetAsyncKeyState(vk_code) & 0x8000 != 0
        
    def is_windows_key_pressed(self):
        """Check if either Windows key is pressed."""
        return (self.is_key_pressed(self.VK_LWIN) or 
                self.is_key_pressed(self.VK_RWIN))
                
    def detection_loop(self):
        """Main detection loop for Windows key events."""
        while self.running:
            try:
                current_pressed = self.is_windows_key_pressed()
                current_time = time.time()
                
                if current_pressed and not self.key_pressed:
                    # Key just pressed
                    self.key_pressed = True
                    self.press_start_time = current_time
                    
                elif not current_pressed and self.key_pressed:
                    # Key just released
                    self.key_pressed = False
                    self.press_start_time = 0
                    
                elif current_pressed and self.key_pressed:
                    # Key is being held
                    hold_time = current_time - self.press_start_time
                    if hold_time >= self.hold_duration:
                        # Trigger callbacks
                        for callback in self.callbacks:
                            try:
                                callback()
                            except Exception as e:
                                print(f"Error in Windows key callback: {e}")
                        
                        # Reset to prevent multiple triggers
                        self.press_start_time = current_time
                        
                time.sleep(0.01)  # Check every 10ms
                
            except Exception as e:
                print(f"Error in Windows key detection: {e}")
                time.sleep(0.1)
                
    def start(self):
        """Start Windows key detection."""
        if not self.running:
            self.running = True
            self.detection_thread = threading.Thread(target=self.detection_loop, daemon=True)
            self.detection_thread.start()
            
    def stop(self):
        """Stop Windows key detection."""
        self.running = False
        if self.detection_thread:
            self.detection_thread.join(timeout=1.0)


class VideoWindowDetector:
    """Detects video playback windows to exclude from inversion."""
    
    COMMON_VIDEO_CLASSES = [
        'MediaPlayerClassicW',  # Media Player Classic
        'VLC DirectX video output',  # VLC
        'Chrome_WidgetWin_1',  # Chrome/YouTube
        'MozillaWindowClass',  # Firefox
        'ApplicationFrameWindow',  # Modern apps
        'Windows.UI.Core.CoreWindow',  # UWP apps
    ]
    
    def __init__(self):
        self.video_windows = set()
        self.last_update = 0
        self.update_interval = 1.0  # Update every second
    
    def is_video_window(self, hwnd):
        """Check if a window is likely displaying video content."""
        try:
            class_name = win32gui.GetClassName(hwnd)
            window_text = win32gui.GetWindowText(hwnd)
            
            # Check common video player classes
            if any(video_class in class_name for video_class in self.COMMON_VIDEO_CLASSES):
                return True
            
            # Check window titles for video indicators
            video_indicators = ['youtube', 'netflix', 'hulu', 'video', 'player', 'vlc', 'media']
            if any(indicator in window_text.lower() for indicator in video_indicators):
                return True
                
            return False
        except:
            return False
    
    def get_video_windows(self):
        """Get list of currently active video windows."""
        current_time = time.time()
        if current_time - self.last_update < self.update_interval:
            return self.video_windows
        
        self.video_windows.clear()
        
        def enum_callback(hwnd, lparam):
            if win32gui.IsWindowVisible(hwnd) and self.is_video_window(hwnd):
                rect = win32gui.GetWindowRect(hwnd)
                self.video_windows.add(rect)
            return True
        
        win32gui.EnumWindows(enum_callback, 0)
        self.last_update = current_time
        return self.video_windows


class MonitorManager:
    """Manages monitor detection and selection."""
    
    def __init__(self):
        self.monitors = []
        self.refresh_monitors()
    
    def refresh_monitors(self):
        """Get list of available monitors."""
        self.monitors = []
        
        def enum_callback(hmonitor, hdc, rect, lparam):
            monitor_info = win32api.GetMonitorInfo(hmonitor)
            self.monitors.append({
                'handle': hmonitor,
                'rect': rect,
                'name': monitor_info.get('Device', f'Monitor {len(self.monitors) + 1}'),
                'primary': monitor_info.get('Flags', 0) & win32con.MONITORINFOF_PRIMARY
            })
            return True
        
        win32api.EnumDisplayMonitors(None, None, enum_callback, 0)
    
    def get_monitor_list(self):
        """Get formatted list of monitors for UI."""
        return [f"{i+1}: {mon['name']} ({'Primary' if mon['primary'] else 'Secondary'})" 
                for i, mon in enumerate(self.monitors)]


class ColorInverter:
    """Handles the color inversion overlay."""
    
    def __init__(self, monitor_rect, video_detector):
        self.monitor_rect = monitor_rect
        self.video_detector = video_detector
        self.running = False
        self.overlay_window = None
        self.canvas = None
        self.inversion_active = False
        self.capture_thread = None
        
    def create_overlay_window(self):
        """Create transparent overlay window for inversion."""
        self.overlay_window = tk.Toplevel()
        self.overlay_window.title("Color Inverter Overlay")
        self.overlay_window.attributes('-alpha', 1.0)
        self.overlay_window.attributes('-topmost', True)
        self.overlay_window.overrideredirect(True)
        
        # Position overlay over target monitor
        x, y, right, bottom = self.monitor_rect
        width = right - x
        height = bottom - y
        
        self.overlay_window.geometry(f"{width}x{height}+{x}+{y}")
        
        # Create canvas for drawing inverted content
        self.canvas = tk.Canvas(
            self.overlay_window, 
            width=width, 
            height=height,
            highlightthickness=0
        )
        self.canvas.pack()
        
    def invert_colors(self, image):
        """Invert colors in image using numpy."""
        return 255 - image
    
    def mask_video_regions(self, image, video_windows):
        """Mask out video window regions from inverted image."""
        if not video_windows:
            return image
            
        mask = np.ones(image.shape[:2], dtype=np.uint8) * 255
        
        for video_rect in video_windows:
            vx, vy, vright, vbottom = video_rect
            mx, my, mright, mbottom = self.monitor_rect
            
            # Convert video window coordinates to monitor-relative coordinates
            rel_x = max(0, vx - mx)
            rel_y = max(0, vy - my)
            rel_right = min(mright - mx, vright - mx)
            rel_bottom = min(mbottom - my, vbottom - my)
            
            if rel_x < rel_right and rel_y < rel_bottom:
                mask[rel_y:rel_bottom, rel_x:rel_right] = 0
        
        # Apply mask to each channel
        for i in range(3):
            image[:, :, i] = cv2.bitwise_and(image[:, :, i], mask)
            
        return image
    
    def capture_and_invert(self):
        """Main loop for capturing and inverting screen content."""
        with mss() as sct:
            monitor = {
                'top': self.monitor_rect[1],
                'left': self.monitor_rect[0], 
                'width': self.monitor_rect[2] - self.monitor_rect[0],
                'height': self.monitor_rect[3] - self.monitor_rect[1]
            }
            
            while self.running:
                try:
                    if self.inversion_active:
                        # Capture screen
                        screenshot = sct.grab(monitor)
                        img = np.array(screenshot)
                        img = cv2.cvtColor(img, cv2.COLOR_BGRA2RGB)
                        
                        # Invert colors
                        inverted = self.invert_colors(img)
                        
                        # Get video windows and mask them out
                        video_windows = self.video_detector.get_video_windows()
                        if video_windows:
                            inverted = self.mask_video_regions(inverted, video_windows)
                        
                        # Convert to PIL Image and display
                        pil_image = Image.fromarray(inverted)
                        photo = ImageTk.PhotoImage(pil_image)
                        
                        if self.canvas and self.overlay_window.winfo_exists():
                            self.canvas.delete("all")
                            self.canvas.create_image(0, 0, anchor=tk.NW, image=photo)
                            self.canvas.image = photo  # Keep reference
                    else:
                        # Clear overlay when not inverting
                        if self.canvas and self.overlay_window.winfo_exists():
                            self.canvas.delete("all")
                    
                    time.sleep(1/30)  # ~30 FPS
                    
                except Exception as e:
                    print(f"Error in capture loop: {e}")
                    break
    
    def set_inversion_active(self, active):
        """Enable or disable color inversion."""
        self.inversion_active = active
        
    def start(self):
        """Start the color inversion system."""
        self.running = True
        self.create_overlay_window()
        
        # Start capture thread
        self.capture_thread = threading.Thread(target=self.capture_and_invert, daemon=True)
        self.capture_thread.start()
    
    def stop(self):
        """Stop the color inversion."""
        self.running = False
        if self.overlay_window:
            self.overlay_window.destroy()


class InverterGUI:
    """Main GUI application."""
    
    def __init__(self):
        self.root = tk.Tk()
        self.root.title("Monitor Color Inverter")
        self.root.geometry("450x350")
        
        self.monitor_manager = MonitorManager()
        self.video_detector = VideoWindowDetector()
        self.windows_key_detector = WindowsKeyDetector(hold_duration=2.0)
        self.inverter = None
        self.inversion_enabled = False
        
        self.setup_ui()
    
    def setup_ui(self):
        """Setup the user interface."""
        # Monitor selection
        tk.Label(self.root, text="Select Monitor to Invert:", font=('Arial', 12)).pack(pady=10)
        
        self.monitor_var = tk.StringVar()
        self.monitor_combo = ttk.Combobox(
            self.root, 
            textvariable=self.monitor_var,
            values=self.monitor_manager.get_monitor_list(),
            state='readonly',
            width=50
        )
        self.monitor_combo.pack(pady=5)
        self.monitor_combo.current(0)
        
        # Refresh button
        tk.Button(
            self.root, 
            text="Refresh Monitors",
            command=self.refresh_monitors
        ).pack(pady=5)
        
        # Control buttons
        button_frame = tk.Frame(self.root)
        button_frame.pack(pady=20)
        
        self.start_button = tk.Button(
            button_frame,
            text="Enable Windows Key Detection",
            command=self.start_detection,
            bg='green',
            fg='white',
            font=('Arial', 10)
        )
        self.start_button.pack(side=tk.LEFT, padx=5)
        
        self.stop_button = tk.Button(
            button_frame,
            text="Disable Detection", 
            command=self.stop_detection,
            bg='red',
            fg='white',
            font=('Arial', 10),
            state=tk.DISABLED
        )
        self.stop_button.pack(side=tk.LEFT, padx=5)
        
        # Status and info
        self.status_label = tk.Label(self.root, text="Ready", font=('Arial', 10))
        self.status_label.pack(pady=10)
        
        info_text = """
        Instructions:
        1. Select the monitor you want to invert
        2. Click 'Enable Windows Key Detection' to start
        3. Hold the Windows key for 2+ seconds to toggle inversion
        4. Video windows will be automatically excluded
        5. Click 'Disable' to stop detection
        
        Note: This program works on Windows only.
        """
        tk.Label(self.root, text=info_text, justify=tk.LEFT, font=('Arial', 9)).pack(pady=10)
    
    def refresh_monitors(self):
        """Refresh the list of available monitors."""
        self.monitor_manager.refresh_monitors()
        self.monitor_combo['values'] = self.monitor_manager.get_monitor_list()
        if self.monitor_combo['values']:
            self.monitor_combo.current(0)
    
    def toggle_inversion(self):
        """Toggle color inversion on/off."""
        if self.inverter:
            self.inversion_enabled = not self.inversion_enabled
            self.inverter.set_inversion_active(self.inversion_enabled)
            
            if self.inversion_enabled:
                selected_index = self.monitor_combo.current()
                selected_monitor = self.monitor_manager.monitors[selected_index]
                self.status_label.config(text=f"Inverting {selected_monitor['name']} - Hold Windows key to toggle")
            else:
                self.status_label.config(text="Detection active - Hold Windows key to toggle")
    
    def start_detection(self):
        """Start Windows key detection and inversion system."""
        try:
            selected_index = self.monitor_combo.current()
            if selected_index < 0:
                messagebox.showerror("Error", "Please select a monitor")
                return
            
            selected_monitor = self.monitor_manager.monitors[selected_index]
            monitor_rect = selected_monitor['rect']
            
            # Create inverter but don't start inversion yet
            self.inverter = ColorInverter(monitor_rect, self.video_detector)
            self.inverter.start()
            
            # Setup Windows key detection
            self.windows_key_detector.add_callback(self.toggle_inversion)
            self.windows_key_detector.start()
            
            self.start_button.config(state=tk.DISABLED)
            self.stop_button.config(state=tk.NORMAL)
            self.status_label.config(text="Detection active - Hold Windows key to toggle")
            
        except Exception as e:
            messagebox.showerror("Error", f"Failed to start detection: {e}")
    
    def stop_detection(self):
        """Stop Windows key detection and color inversion."""
        if self.windows_key_detector:
            self.windows_key_detector.stop()
            
        if self.inverter:
            self.inverter.stop()
            self.inverter = None
            
        self.inversion_enabled = False
        self.start_button.config(state=tk.NORMAL)
        self.stop_button.config(state=tk.DISABLED)
        self.status_label.config(text="Ready")
    
    def run(self):
        """Run the application."""
        self.root.protocol("WM_DELETE_WINDOW", self.on_closing)
        self.root.mainloop()
    
    def on_closing(self):
        """Handle application closing."""
        if self.windows_key_detector:
            self.windows_key_detector.stop()
        if self.inverter:
            self.inverter.stop()
        self.root.destroy()


def main():
    """Main entry point."""
    if sys.platform != 'win32':
        print("This program is designed for Windows only.")
        return
    
    try:
        app = InverterGUI()
        app.run()
    except Exception as e:
        print(f"Error starting application: {e}")


if __name__ == "__main__":
    main()