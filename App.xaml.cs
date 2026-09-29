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
        private static bool _ownsMutex = false;
        private static EventWaitHandle? _showWindowEvent;
        private static RegisteredWaitHandle? _registeredWait;
        private TrayIconManager? _trayIconManager;
        private MainWindow? _mainWindow;

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern bool AttachConsole(int dwProcessId);

        protected override void OnStartup(StartupEventArgs e)
        {
            DispatcherUnhandledException += (s, args) =>
            {
                try
                {
                    string msg = $"Unhandled UI Exception:\n{args.Exception}";
                    System.IO.File.AppendAllText("crash_log.txt", $"[{DateTime.Now}] {msg}\n\n");
                    MessageBox.Show(args.Exception.Message, "AudioSwitcher Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                catch { }
                args.Handled = true;
            };

            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                try
                {
                    string msg = $"Unhandled AppDomain Exception:\n{args.ExceptionObject}";
                    System.IO.File.AppendAllText("crash_log.txt", $"[{DateTime.Now}] {msg}\n\n");
                }
                catch { }
            };

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
            const string eventName = "AudioSwitcher_App_ShowWindow_Event";

            bool isNewInstance;
            try
            {
                _mutex = new Mutex(true, mutexName, out isNewInstance);
                _ownsMutex = isNewInstance;
            }
            catch (Exception)
            {
                isNewInstance = false;
                _ownsMutex = false;
            }

            if (!isNewInstance)
            {
                // Signal the existing running instance to bring its window to the foreground
                try
                {
                    using var ev = EventWaitHandle.OpenExisting(eventName);
                    ev.Set();
                }
                catch
                {
                    MessageBox.Show(
                        "AudioSwitcher is already running in your System Tray.\nCheck the bottom-right taskbar notification area.",
                        "AudioSwitcher",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information
                    );
                }

                Shutdown(0);
                return;
            }

            // Create the named event so subsequent launches can wake up / restore this instance
            try
            {
                _showWindowEvent = new EventWaitHandle(false, EventResetMode.AutoReset, eventName);
                _registeredWait = ThreadPool.RegisterWaitForSingleObject(
                    _showWindowEvent,
                    (state, timedOut) =>
                    {
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            ShowMainWindow();
                        }));
                    },
                    null,
                    Timeout.Infinite,
                    false
                );
            }
            catch { }

            // Initialize services
            _ = SettingsService.Instance;
            _ = AudioDeviceManager.Instance;
            _ = PrioritySwitcherService.Instance;

            // Initialize system tray icon
            _trayIconManager = new TrayIconManager();

            // Check if started with --minimized flag (e.g. from Windows Startup)
            bool startMinimized = e.Args.Contains("--minimized");

            if (!startMinimized)
            {
                ShowMainWindow();
            }
        }

        public static App CurrentApp => (App)Current;

        public void ShowMainWindow()
        {
            Dispatcher.Invoke(() =>
            {
                if (_mainWindow == null || _mainWindow.IsClosed)
                {
                    _mainWindow = new MainWindow();
                }

                _mainWindow.ShowAndActivate();
            });
        }

        public void ExitApplication()
        {
            Dispatcher.Invoke(() =>
            {
                _trayIconManager?.Dispose();
                if (_mainWindow != null && !_mainWindow.IsClosed)
                {
                    _mainWindow.ExitApplication();
                }
                else
                {
                    Shutdown();
                }
            });
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                _registeredWait?.Unregister(null);
                _showWindowEvent?.Dispose();
            }
            catch { }

            _trayIconManager?.Dispose();
            AudioDeviceManager.Instance.Dispose();

            if (_ownsMutex && _mutex != null)
            {
                try
                {
                    _mutex.ReleaseMutex();
                }
                catch { }
            }

            try
            {
                _mutex?.Dispose();
            }
            catch { }

            base.OnExit(e);
        }
    }
}
