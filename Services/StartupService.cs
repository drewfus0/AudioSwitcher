using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace AudioSwitcher.Services
{
    public static class StartupService
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "AudioSwitcher";

        public static bool IsStartupEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
                return key?.GetValue(AppName) != null;
            }
            catch
            {
                return false;
            }
        }

        public static void SetStartup(bool enable)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
                if (key == null) return;

                if (enable)
                {
                    string exePath = Process.GetCurrentProcess().MainModule?.FileName 
                        ?? Path.Combine(AppContext.BaseDirectory, "AudioSwitcher.exe");
                    key.SetValue(AppName, $"\"{exePath}\" --minimized");
                }
                else
                {
                    key.DeleteValue(AppName, false);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to update registry startup key: {ex.Message}");
            }
        }
    }
}
