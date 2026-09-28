using System;
using System.ComponentModel;
using System.Windows;
using AudioSwitcher.CoreAudio;
using AudioSwitcher.Services;

namespace AudioSwitcher.UI
{
    public partial class MainWindow : Window
    {
        private bool _isExplicitExit = false;

        public MainWindow()
        {
            InitializeComponent();

            Loaded += MainWindow_Loaded;
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
            ChkOptStartWithWindows.IsChecked = StartupService.IsStartupEnabled();
            ChkOptStartMinimized.IsChecked = settings.StartMinimized;
            ChkOptCloseToTray.IsChecked = settings.CloseToTray;

            // Register events
            AudioDeviceManager.Instance.DevicesUpdated += OnDevicesUpdated;
            PrioritySwitcherService.Instance.DeviceAutoSwitched += OnDeviceAutoSwitched;

            UpdateStatus("Listening for audio device changes");
        }

        private void OnDevicesUpdated()
        {
            Dispatcher.Invoke(() =>
            {
                CtrlOutputSound.ReloadData();
                CtrlOutputComms.ReloadData();
                CtrlInputSound.ReloadData();
                CtrlInputComms.ReloadData();
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

            bool startWithWin = ChkOptStartWithWindows.IsChecked == true;
            settings.StartWithWindows = startWithWin;
            StartupService.SetStartup(startWithWin);

            SettingsService.Instance.Save();
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

        private void Window_StateChanged(object sender, EventArgs e)
        {
            if (WindowState == WindowState.Minimized && SettingsService.Instance.Settings.CloseToTray)
            {
                Hide();
            }
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            if (!_isExplicitExit && SettingsService.Instance.Settings.CloseToTray)
            {
                e.Cancel = true;
                Hide();
            }
        }

        public void ShowAndActivate()
        {
            Show();
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }
            Activate();
            Focus();
        }

        public void ExitApplication()
        {
            _isExplicitExit = true;
            Close();
            Application.Current.Shutdown();
        }
    }
}
