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
            ChkOptMatchOutputComms.IsChecked = settings.MatchOutputCommsToSound;
            ChkOptMatchInputComms.IsChecked = settings.MatchInputCommsToSound;
            ChkOptStartWithWindows.IsChecked = StartupService.IsStartupEnabled();
            ChkOptStartMinimized.IsChecked = settings.StartMinimized;
            ChkOptCloseToTray.IsChecked = settings.CloseToTray;

            // Register events
            AudioDeviceManager.Instance.DevicesUpdated += OnDevicesUpdated;
            PrioritySwitcherService.Instance.DeviceAutoSwitched += OnDeviceAutoSwitched;
            SettingsService.Instance.SettingsChanged += OnSettingsChanged;

            RefreshHiddenDevices();
            UpdateStatus("Listening for audio device changes");
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
            var ignored = SettingsService.Instance.Settings.IgnoredDevices;
            ItemsHiddenDevices.ItemsSource = null;
            ItemsHiddenDevices.ItemsSource = ignored;

            TxtNoHiddenDevices.Visibility = (ignored.Count == 0) ? Visibility.Visible : Visibility.Collapsed;
            BtnUnhideAll.IsEnabled = ignored.Count > 0;

            // Populate CmbDevicesToHide with all detected devices not already ignored
            var allDevices = AudioDeviceManager.Instance.GetDevices();
            CmbDevicesToHide.Items.Clear();

            foreach (var d in allDevices)
            {
                if (!SettingsService.Instance.IsDeviceIgnored(d.Id, d.Name))
                {
                    string flowLabel = d.DataFlow == EDataFlow.eRender ? "Output" : "Input";
                    CmbDevicesToHide.Items.Add(new System.Windows.Controls.ComboBoxItem
                    {
                        Content = $"[{flowLabel}] {d.Name} ({d.StatusText})",
                        Tag = d
                    });
                }
            }

            if (CmbDevicesToHide.Items.Count > 0)
            {
                CmbDevicesToHide.SelectedIndex = 0;
                BtnHideDeviceInSettings.IsEnabled = true;
            }
            else
            {
                CmbDevicesToHide.Items.Add(new System.Windows.Controls.ComboBoxItem
                {
                    Content = "(All detected devices are hidden)",
                    IsEnabled = false
                });
                CmbDevicesToHide.SelectedIndex = 0;
                BtnHideDeviceInSettings.IsEnabled = false;
            }
        }

        private void BtnUnhideDevice_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.Tag is IgnoredDeviceEntry entry)
            {
                SettingsService.Instance.UnignoreDevice(entry.Id, entry.Name);
                PrioritySwitcherService.Instance.EvaluateAllPriorities();
                UpdateStatus($"Restored {entry.Name} ({DateTime.Now:T})");
            }
        }

        private void BtnUnhideAll_Click(object sender, RoutedEventArgs e)
        {
            SettingsService.Instance.UnignoreAllDevices();
            PrioritySwitcherService.Instance.EvaluateAllPriorities();
            UpdateStatus($"Restored all hidden devices ({DateTime.Now:T})");
        }

        private void BtnHideDeviceInSettings_Click(object sender, RoutedEventArgs e)
        {
            if (CmbDevicesToHide.SelectedItem is System.Windows.Controls.ComboBoxItem item && item.Tag is AudioDevice device)
            {
                SettingsService.Instance.IgnoreDevice(device.Id, device.Name);
                PrioritySwitcherService.Instance.EvaluateAllPriorities();
                UpdateStatus($"Hidden {device.Name} ({DateTime.Now:T})");
            }
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
                ExitApplication();
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            IsClosed = true;

            AudioDeviceManager.Instance.DevicesUpdated -= OnDevicesUpdated;
            PrioritySwitcherService.Instance.DeviceAutoSwitched -= OnDeviceAutoSwitched;
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
            Focus();
        }

        public void ExitApplication()
        {
            _isExplicitExit = true;
            if (!IsClosed)
            {
                Close();
            }
            Application.Current.Shutdown();
        }
    }
}
