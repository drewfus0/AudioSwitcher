using System;
using System.Linq;
using System.Threading;
using System.Windows;
using AudioSwitcher.CoreAudio;
using AudioSwitcher.Services;
using AudioSwitcher.UI;

namespace AudioSwitcher
{
    public partial class App : System.Windows.Application
    {
        private static Mutex? _mutex;
        private TrayIconManager? _trayIconManager;
        private MainWindow? _mainWindow;

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern bool AttachConsole(int dwProcessId);

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            if (e.Args.Contains("--test"))
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("==========================================");
                sb.AppendLine("    AUDIOSWITCHER - COREAUDIO TEST        ");
                sb.AppendLine("==========================================");
                var mgr = AudioDeviceManager.Instance;
                mgr.RefreshDevices();
                var devices = mgr.GetDevices();
                sb.AppendLine($"Total audio devices detected: {devices.Count}\n");

                sb.AppendLine("--- OUTPUT DEVICES (Render) ---");
                foreach (var d in devices.Where(x => x.DataFlow == EDataFlow.eRender))
                {
                    string def = d.IsDefaultSound ? "[DEFAULT SOUND] " : "";
                    if (d.IsDefaultCommunications) def += "[DEFAULT COMMS] ";
                    sb.AppendLine($"  * {d.Name} ({d.StatusText}) {def}");
                }

                sb.AppendLine("\n--- INPUT DEVICES (Capture) ---");
                foreach (var d in devices.Where(x => x.DataFlow == EDataFlow.eCapture))
                {
                    string def = d.IsDefaultSound ? "[DEFAULT SOUND] " : "";
                    if (d.IsDefaultCommunications) def += "[DEFAULT COMMS] ";
                    sb.AppendLine($"  * {d.Name} ({d.StatusText}) {def}");
                }
                sb.AppendLine("==========================================\n");

                string output = sb.ToString();
                System.IO.File.WriteAllText("test_output.txt", output);
                Shutdown();
                return;
            }

            if (e.Args.Contains("--test-switch"))
            {
                var sb = new System.Text.StringBuilder();
                var mgr = AudioDeviceManager.Instance;
                mgr.RefreshDevices();
                var activeRender = mgr.GetDevices(EDataFlow.eRender).Where(d => d.IsActive).ToList();
                if (activeRender.Count >= 2)
                {
                    var original = activeRender.FirstOrDefault(d => d.IsDefaultSound) ?? activeRender[0];
                    var other = activeRender.FirstOrDefault(d => d.Id != original.Id) ?? activeRender[1];

                    sb.AppendLine($"Original default: {original.Name}");
                    sb.AppendLine($"Switching to: {other.Name}...");

                    bool s1 = mgr.SetDefaultDevice(other.Id, AudioCategory.OutputSound);
                    sb.AppendLine($"Switch result: {s1}");

                    // Check if it updated
                    mgr.RefreshDevices();
                    var newDef = mgr.GetDefaultDevice(AudioCategory.OutputSound);
                    sb.AppendLine($"New default reported by CoreAudio: {newDef?.Name}");

                    // Restore original
                    sb.AppendLine($"Restoring original default: {original.Name}...");
                    bool s2 = mgr.SetDefaultDevice(original.Id, AudioCategory.OutputSound);
                    sb.AppendLine($"Restore result: {s2}");

                    mgr.RefreshDevices();
                    var restoredDef = mgr.GetDefaultDevice(AudioCategory.OutputSound);
                    sb.AppendLine($"Final default reported: {restoredDef?.Name}");
                }
                else
                {
                    sb.AppendLine("Less than 2 active render devices available to test switch.");
                }

                string output = sb.ToString();
                System.IO.File.WriteAllText("switch_test_output.txt", output);
                Shutdown();
                return;
            }

            // Single-instance enforcement
            const string mutexName = "AudioSwitcher_App_SingleInstance_Mutex";
            _mutex = new Mutex(true, mutexName, out bool isNewInstance);
            if (!isNewInstance)
            {
                // Already running
                MessageBox.Show(
                    "AudioSwitcher is already running in your System Tray.\nCheck the bottom-right taskbar notification area.",
                    "AudioSwitcher",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
                Shutdown(0);
                return;
            }

            // Initialize services
            _ = SettingsService.Instance;
            _ = AudioDeviceManager.Instance;
            _ = PrioritySwitcherService.Instance;

            // Create MainWindow
            _mainWindow = new MainWindow();

            // Initialize system tray icon
            _trayIconManager = new TrayIconManager(_mainWindow);

            // Check if started with --minimized flag (e.g. from Windows Startup)
            bool startMinimized = e.Args.Contains("--minimized");

            if (!startMinimized)
            {
                _mainWindow.ShowAndActivate();
            }
            else
            {
                // App is loaded and living in tray; MainWindow stays hidden
                _mainWindow.WindowState = WindowState.Minimized;
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _trayIconManager?.Dispose();
            AudioDeviceManager.Instance.Dispose();
            _mutex?.ReleaseMutex();
            _mutex?.Dispose();
            base.OnExit(e);
        }
    }
}
