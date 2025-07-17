using System;
using System.Windows;

namespace ColorInverter
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            
            // Ensure running on Windows
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
            {
                MessageBox.Show("This application is designed for Windows only.", "Platform Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
                return;
            }
        }
    }
}