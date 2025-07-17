# YRA Color Inverter for Windows

A Windows WPF application that inverts colors on a selected monitor while automatically excluding video playback windows.

## Features

- **Multi-monitor support**: Select which monitor to invert
- **Windows key activation**: Hold Windows key for 2+ seconds to toggle inversion
- **Video window detection**: Automatically excludes common video players (VLC, Chrome/YouTube, Firefox, etc.)
- **Real-time inversion**: ~30 FPS color inversion overlay
- **Modern WPF GUI**: Clean interface for monitor selection and control

## Requirements

- Windows OS
- .NET 6 Runtime

## Installation

1. Clone or download this repository
2. Build the application:
   ```bash
   dotnet build ColorInverter.csproj
   ```

## Usage

1. Run the program:
   ```bash
   dotnet run
   ```

2. Select the monitor you want to invert from the dropdown

3. Click "Enable Windows Key Detection" to start the system

4. **Hold the Windows key for 2+ seconds** to toggle color inversion on/off

5. The program will automatically detect and exclude video windows

6. Click "Disable Detection" to stop

## How It Works

1. **Monitor Detection**: Uses Windows API (P/Invoke) to enumerate available monitors
2. **Windows Key Detection**: Monitors Windows key press/hold events using GetAsyncKeyState
3. **Screen Capture**: Captures the target monitor using Windows GDI API
4. **Color Inversion**: Inverts RGB values (255 - original_value) using unsafe bitmap operations
5. **Video Detection**: Identifies video windows by class name and window title
6. **Masking**: Excludes video regions from the inverted overlay
7. **Overlay**: Displays the result as a transparent WPF overlay window

## Supported Video Players

- VLC Media Player
- Media Player Classic
- Chrome (YouTube, Netflix, etc.)
- Firefox video players
- Modern Windows apps (UWP)
- Most common video applications

## Performance Notes

- Runs at approximately 30 FPS
- Optimized for minimal CPU usage with unsafe code blocks
- Video window detection updates every second
- Asynchronous processing to keep UI responsive
- Uses efficient Windows GDI calls for screen capture

## Troubleshooting

- Ensure you're running on Windows
- Install .NET 6 Runtime if not already installed
- Run as administrator if screen capture fails
- Close other screen capture applications if conflicts occur

## Technical Details

The program uses:
- **WPF**: Modern Windows UI framework with XAML
- **P/Invoke**: Direct Windows API calls for system integration
- **System.Drawing.Common**: Bitmap manipulation and screen capture
- **Unsafe code blocks**: High-performance pixel manipulation
- **Async/await**: Responsive UI with background processing
- **Windows GDI API**: Efficient screen capture via BitBlt
