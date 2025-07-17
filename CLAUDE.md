# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is a Windows WPF application that inverts colors on a selected monitor while automatically excluding video playback windows. The application is built with C# and .NET 8.0-windows, using WPF for the UI and Windows Forms for monitor enumeration.

## Build and Development Commands

### Build Commands
```bash
dotnet build ColorInverter.csproj
```

### Run Commands
```bash
dotnet run
```

### Python Component (Optional)
For the Python monitor inverter component:
```bash
pip install -r requirements.txt
python monitor_inverter.py
```

## Core Architecture

### Main Components

1. **MainWindow** (`MainWindow.xaml.cs`): The primary WPF window that provides the user interface for monitor selection and control. Contains the main application logic for toggling inversion and managing the overlay windows.

2. **ColorInverterCore** (`ColorInverterCore.cs`): The core inversion engine that handles:
   - Screen capture using Windows GDI API
   - Color inversion using unsafe bitmap manipulation
   - Video window masking to exclude video regions
   - Overlay window management and positioning

3. **MonitorManager** (`MonitorManager.cs`): Manages multi-monitor detection and DPI scaling:
   - Enumerates monitors using Windows API (EnumDisplayMonitors)
   - Handles DPI scaling calculations for proper positioning
   - Provides monitor information with both physical and logical coordinates

4. **VideoWindowDetector** (`VideoWindowDetector.cs`): Detects video playback windows to exclude them from inversion:
   - Identifies common video players (VLC, Chrome, Firefox, etc.)
   - Updates video window list every second
   - Provides window rectangles for masking

5. **HotkeyDetector** (`WindowsKeyDetector.cs`): Implements global keyboard hook for Ctrl+Shift+I hotkey detection using low-level Windows API hooks with proper thread marshalling for UI updates.

### Key Technical Details

- **Performance**: Runs at ~30 FPS using optimized unsafe code blocks for pixel manipulation
- **DPI Awareness**: Full per-monitor DPI scaling support using Windows API with proper coordinate conversion
- **Video Detection**: Identifies video windows by class name and title matching
- **Overlay System**: Creates transparent WPF overlay windows positioned correctly on target monitors with proper DPI scaling
- **Memory Management**: Proper disposal of GDI resources and bitmap data
- **Thread Safety**: Hotkey callbacks properly marshalled to UI thread using Dispatcher.Invoke
- **Nullable Reference Types**: Enabled with proper null checking for Screen and monitor references

### Architecture Flow

1. User selects monitor from dropdown (populated by MonitorManager)
2. User clicks Start to initialize ColorInverterCore and begin hotkey detection
3. HotkeyDetector registers global Ctrl+Shift+I hotkey with proper callback management
4. On hotkey press, ToggleInversion is called and marshalled to UI thread
5. Overlay window is created/destroyed with proper DPI scaling and positioning
6. Screen capture and color inversion run at 30 FPS when overlay is active
7. VideoWindowDetector identifies video windows to exclude from inversion
8. Proper cleanup occurs when stopping detection or closing application

## Key Files to Understand

- `ColorInverter.csproj`: Project configuration with .NET 8.0-windows, WPF, and unsafe code enabled
- `App.xaml` and `App.xaml.cs`: WPF application startup configuration
- `MainWindow.xaml`: UI layout with monitor dropdown and control buttons
- `requirements.txt`: Python dependencies for optional Python component

## Windows API Dependencies

The application heavily uses P/Invoke for Windows API calls:
- User32.dll: Window enumeration, DPI detection, keyboard hooks, monitor positioning
- GDI32.dll: Screen capture and bitmap operations
- Shcore.dll: Per-monitor DPI scaling
- Kernel32.dll: Module handle retrieval for hook installation

## Recent Fixes and Improvements

### Build Error Fixes
- **CS0104 Ambiguous References**: Fixed by adding explicit namespace qualifiers for `MessageBox` (System.Windows.MessageBox), `Application` (System.Windows.Application), `HorizontalAlignment` and `VerticalAlignment` (System.Windows.*)
- **CS0176 Static Member Access**: Resolved alignment enum access issues
- **CS8604 Nullable Reference**: Added proper null checking for Screen.PrimaryScreen and monitor references

### Thread Safety Improvements
- **Hotkey Callback Threading**: Added Dispatcher.CheckAccess() and Dispatcher.Invoke() to ensure UI updates occur on the correct thread
- **Callback Management**: Implemented proper callback registration/deregistration to prevent duplicate callbacks
- **Resource Cleanup**: Enhanced cleanup in StopButton_Click to properly dispose of overlay windows and timers

### DPI Scaling Enhancements
- **Monitor Positioning**: Improved coordinate conversion between physical and logical pixels
- **Null Safety**: Added fallback logic for monitor detection when primary screen is unavailable
- **Error Handling**: Added graceful handling for edge cases in DPI scaling calculations