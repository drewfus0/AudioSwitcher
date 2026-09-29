using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using AudioSwitcher.CoreAudio;
using AudioSwitcher.Services;

namespace AudioSwitcher.UI
{
    public partial class MainWindow : Window
    {
        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        private bool _isExplicitExit = false;

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            try
            {
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                int useDarkMode = 1;
                if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDarkMode, sizeof(int)) != 0)
                {
                    DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref useDarkMode, sizeof(int));
                }
            }
            catch { }
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Initialize 4 category tabs
            CtrlOutputSound.Initialize(AudioCategory.OutputSound);
            CtrlOutputComms.Initialize(AudioCategory.OutputCommunications);
            CtrlInputSound.Initialize(AudioCategory.InputSound);
            CtrlInputComms.Initialize(AudioCategory.InputCommunications);

            // Load settings into UI
            var settings = SettingsService.Instance.Settings;
            ChkMasterAutoSwitch.IsChecked = settings.AutoSwitchEnabled;
            ChkOptShowNotifications.IsChecked = settings.ShowSwitchNotifications;
            ChkOptMatchOutputComms.IsChecked = settings.MatchOutputCommsToSound;
            ChkOptMatchInputComms.IsChecked = settings.MatchInputCommsToSound;
            ChkOptStartWithWindows.IsChecked = StartupService.IsStartupEnabled();
            ChkOptStartMinimized.IsChecked = settings.StartMinimized;
            ChkOptCloseToTray.IsChecked = settings.CloseToTray;
            ChkOptVolumeMapping.IsChecked = settings.EnableVolumeMappingOnSwitch;

            // Register events
            AudioDeviceManager.Instance.DevicesUpdated += OnDevicesUpdated;
            PrioritySwitcherService.Instance.DeviceAutoSwitched += OnDeviceAutoSwitched;
            PrioritySwitcherService.Instance.TemporaryOverrideChanged += OnTemporaryOverrideChanged;
            SettingsService.Instance.SettingsChanged += OnSettingsChanged;

            RefreshHiddenDevices();
            UpdateStatus("Listening for audio device changes");
        }

        private void OnTemporaryOverrideChanged(AudioCategory category, string? deviceId)
        {
            Dispatcher.Invoke(() =>
            {
                CtrlOutputSound.ReloadData();
                CtrlOutputComms.ReloadData();
                CtrlInputSound.ReloadData();
                CtrlInputComms.ReloadData();
                if (!string.IsNullOrEmpty(deviceId))
                {
                    var dev = AudioDeviceManager.Instance.GetDeviceById(deviceId);
                    string name = dev?.Name ?? "Device";
                    UpdateStatus($"Temporary override activated for {category.GetShortName()} ({name})");
                }
                else
                {
                    UpdateStatus($"Temporary override cleared for {category.GetShortName()}");
                }
            });
        }

        private void OnSettingsChanged()
        {
            Dispatcher.Invoke(() =>
            {
                var settings = SettingsService.Instance.Settings;
                ChkOptMatchOutputComms.IsChecked = settings.MatchOutputCommsToSound;
                ChkOptMatchInputComms.IsChecked = settings.MatchInputCommsToSound;

                CtrlOutputSound.ReloadData();
                CtrlOutputComms.ReloadData();
                CtrlInputSound.ReloadData();
                CtrlInputComms.ReloadData();
                RefreshHiddenDevices();
            });
        }

        private void RefreshHiddenDevices()
        {
            var allDevices = AudioDeviceManager.Instance.GetDevices();

            int totalCount = allDevices.Count;
            int hiddenCount = allDevices.Count(d => SettingsService.Instance.IsDeviceIgnored(d.Id, d.Name));
            int visibleCount = totalCount - hiddenCount;

            if (TxtDeviceSummaryStats != null)
            {
                TxtDeviceSummaryStats.Text = $"Total detected: {totalCount} • Visible: {visibleCount} • Hidden: {hiddenCount}";
            }

            if (BtnUnhideAll != null)
            {
                BtnUnhideAll.IsEnabled = hiddenCount > 0;
            }

            string searchText = TxtSearchSystemDevices?.Text?.Trim() ?? string.Empty;
            bool filterOutputs = RadFilterOutputs?.IsChecked == true;
            bool filterInputs = RadFilterInputs?.IsChecked == true;
            bool filterHidden = RadFilterHidden?.IsChecked == true;

            var items = new System.Collections.Generic.List<SystemDeviceItem>();

            foreach (var d in allDevices)
            {
                bool isIgnored = SettingsService.Instance.IsDeviceIgnored(d.Id, d.Name);

                if (filterOutputs && d.DataFlow != EDataFlow.eRender) continue;
                if (filterInputs && d.DataFlow != EDataFlow.eCapture) continue;
                if (filterHidden && !isIgnored) continue;

                if (!string.IsNullOrEmpty(searchText) && !d.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                items.Add(new SystemDeviceItem
                {
                    Id = d.Id,
                    Name = d.Name,
                    DataFlow = d.DataFlow,
                    StatusText = d.StatusText,
                    IsActive = d.IsActive,
                    IsIgnored = isIgnored
                });
            }

            // Sort: Visible first, then active first, then by name
            items = items.OrderBy(x => x.IsIgnored)
                         .ThenByDescending(x => x.IsActive)
                         .ThenBy(x => x.DataFlow)
                         .ThenBy(x => x.Name)
                         .ToList();

            if (ItemsAllSystemDevices != null)
            {
                ItemsAllSystemDevices.ItemsSource = null;
                ItemsAllSystemDevices.ItemsSource = items;
            }

            if (TxtNoMatchingSystemDevices != null)
            {
                TxtNoMatchingSystemDevices.Visibility = (items.Count == 0) ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void DeviceFilter_Changed(object sender, RoutedEventArgs e)
        {
            RefreshHiddenDevices();
        }

        private void TxtSearchSystemDevices_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            RefreshHiddenDevices();
        }

        private void BtnToggleDeviceVisibility_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.Tag is SystemDeviceItem item)
            {
                if (item.IsIgnored)
                {
                    SettingsService.Instance.UnignoreDevice(item.Id, item.Name);
                    UpdateStatus($"Restored {item.Name} ({DateTime.Now:T})");
                }
                else
                {
                    SettingsService.Instance.IgnoreDevice(item.Id, item.Name);
                    UpdateStatus($"Hidden {item.Name} ({DateTime.Now:T})");
                }

                PrioritySwitcherService.Instance.EvaluateAllPriorities();
                RefreshHiddenDevices();
            }
        }

        private void BtnUnhideAll_Click(object sender, RoutedEventArgs e)
        {
            SettingsService.Instance.UnignoreAllDevices();
            PrioritySwitcherService.Instance.EvaluateAllPriorities();
            RefreshHiddenDevices();
            UpdateStatus($"Restored all hidden devices ({DateTime.Now:T})");
        }

        private void OnDevicesUpdated()
        {
            Dispatcher.Invoke(() =>
            {
                CtrlOutputSound.ReloadData();
                CtrlOutputComms.ReloadData();
                CtrlInputSound.ReloadData();
                CtrlInputComms.ReloadData();
                RefreshHiddenDevices();
                UpdateStatus($"Device list updated ({DateTime.Now:T})");
            });
        }

        private void OnDeviceAutoSwitched(AudioCategory category, AudioDevice device)
        {
            Dispatcher.Invoke(() =>
            {
                CtrlOutputSound.ReloadData();
                CtrlOutputComms.ReloadData();
                CtrlInputSound.ReloadData();
                CtrlInputComms.ReloadData();
                UpdateStatus($"Auto-switched {category.GetShortName()} to {device.Name} ({DateTime.Now:T})");
            });
        }

        private void UpdateStatus(string message)
        {
            TxtStatus.Text = message;
        }

        private void ChkMasterAutoSwitch_Changed(object sender, RoutedEventArgs e)
        {
            bool enabled = ChkMasterAutoSwitch.IsChecked == true;
            SettingsService.Instance.Settings.AutoSwitchEnabled = enabled;
            SettingsService.Instance.Save();

            if (enabled)
            {
                PrioritySwitcherService.Instance.EvaluateAllPriorities();
            }
        }

        private void SettingsOption_Changed(object sender, RoutedEventArgs e)
        {
            var settings = SettingsService.Instance.Settings;
            settings.ShowSwitchNotifications = ChkOptShowNotifications.IsChecked == true;
            settings.StartMinimized = ChkOptStartMinimized.IsChecked == true;
            settings.CloseToTray = ChkOptCloseToTray.IsChecked == true;
            settings.EnableVolumeMappingOnSwitch = ChkOptVolumeMapping.IsChecked == true;

            bool prevMatchOut = settings.MatchOutputCommsToSound;
            bool prevMatchIn = settings.MatchInputCommsToSound;
            bool newMatchOut = ChkOptMatchOutputComms.IsChecked == true;
            bool newMatchIn = ChkOptMatchInputComms.IsChecked == true;

            settings.MatchOutputCommsToSound = newMatchOut;
            settings.MatchInputCommsToSound = newMatchIn;

            bool startWithWin = ChkOptStartWithWindows.IsChecked == true;
            settings.StartWithWindows = startWithWin;
            StartupService.SetStartup(startWithWin);

            SettingsService.Instance.Save();

            if (prevMatchOut != newMatchOut || prevMatchIn != newMatchIn)
            {
                PrioritySwitcherService.Instance.EvaluateAllPriorities();
            }
        }

        private void BtnOpenVolumeCalibration_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var win = new VolumeCalibrationWindow();
                if (IsLoaded && IsVisible)
                {
                    win.Owner = this;
                }
                win.ShowDialog();
                CtrlOutputSound.ReloadData();
                CtrlOutputComms.ReloadData();
                CtrlInputSound.ReloadData();
                CtrlInputComms.ReloadData();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error opening VolumeCalibrationWindow: {ex}");
                MessageBox.Show($"Unable to open Volume Calibration Lab:\n{ex.Message}", "AudioSwitcher", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnRescanDevices_Click(object sender, RoutedEventArgs e)
        {
            AudioDeviceManager.Instance.RefreshDevices();
            PrioritySwitcherService.Instance.EvaluateAllPriorities();
            UpdateStatus("Manual rescan completed.");
        }

        private void BtnMinimizeToTray_Click(object sender, RoutedEventArgs e)
        {
            Hide();
        }

        public bool IsClosed { get; private set; }

        private void Window_StateChanged(object sender, EventArgs e)
        {
            if (WindowState == WindowState.Minimized && SettingsService.Instance.Settings.CloseToTray)
            {
                Hide();
            }
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            if (_isExplicitExit)
            {
                return;
            }

            if (SettingsService.Instance.Settings.CloseToTray)
            {
                e.Cancel = true;
                Hide();
            }
            else
            {
                _isExplicitExit = true;
                Application.Current.Shutdown();
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            IsClosed = true;

            AudioDeviceManager.Instance.DevicesUpdated -= OnDevicesUpdated;
            PrioritySwitcherService.Instance.DeviceAutoSwitched -= OnDeviceAutoSwitched;
            PrioritySwitcherService.Instance.TemporaryOverrideChanged -= OnTemporaryOverrideChanged;
            SettingsService.Instance.SettingsChanged -= OnSettingsChanged;
        }

        public void ShowAndActivate()
        {
            if (IsClosed)
            {
                return;
            }

            Show();
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }
            Activate();
            Topmost = true;
            Topmost = false;
            Focus();
        }

        public void ExitApplication()
        {
            _isExplicitExit = true;
            try
            {
                if (!IsClosed)
                {
                    Close();
                }
            }
            catch { }
            Application.Current.Shutdown();
        }
    }

    public class SystemDeviceItem
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public EDataFlow DataFlow { get; set; }
        public string FlowIcon => DataFlow == EDataFlow.eRender ? "🔊" : "🎤";
        public string FlowLabel => DataFlow == EDataFlow.eRender ? "Output (Playback)" : "Input (Microphone / Capture)";
        public string StatusText { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public bool IsIgnored { get; set; }

        public string VisibilityBadgeText => IsIgnored ? "🚫 Hidden / Ignored" : "👁️ Visible";
        public string VisibilityBadgeBackground => IsIgnored ? "#3F1D1D" : "#14532D";
        public string VisibilityBadgeForeground => IsIgnored ? "#F87171" : "#4ADE80";
        public string VisibilityBadgeBorder => IsIgnored ? "#7F1D1D" : "#166534";

        public string StatusBadgeBackground => IsActive ? "#14532D" : "#272730";
        public string StatusBadgeForeground => IsActive ? "#4ADE80" : "#9CA3AF";

        public string CardBorderBrush => IsIgnored ? "#452222" : (IsActive ? "#2E384D" : "#2E2E38");
        public string CardBackground => IsIgnored ? "#1F1818" : "#24242D";

        public string ActionButtonText => IsIgnored ? "✓ Unhide / Restore" : "🚫 Hide Device";
        public string ActionButtonTooltip => IsIgnored ? "Restore this device so it appears in priority lists and tray menus" : "Hide this device and exclude it from auto-switching";
    }
}
