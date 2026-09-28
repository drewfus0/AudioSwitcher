using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AudioSwitcher.CoreAudio;
using AudioSwitcher.Services;

namespace AudioSwitcher.UI
{
    public partial class PriorityListControl : System.Windows.Controls.UserControl
    {
        private AudioCategory _category;
        private readonly ObservableCollection<PriorityItemViewModel> _items = new();
        private bool _isUpdatingUi = false;

        public AudioCategory Category => _category;

        public PriorityListControl()
        {
            InitializeComponent();
            ItemsPriorityList.ItemsSource = _items;
        }

        public void Initialize(AudioCategory category)
        {
            _category = category;

            TxtCategoryTitle.Text = category.GetDisplayName();
            TxtCategoryDescription.Text = category switch
            {
                AudioCategory.OutputSound => "Prioritized devices for system sounds, games, and media playback.",
                AudioCategory.OutputCommunications => "Prioritized devices for calls (Discord, Teams, Zoom, Skype).",
                AudioCategory.InputSound => "Prioritized microphones for general sound recording.",
                AudioCategory.InputCommunications => "Prioritized microphones for calls (Discord, Teams, Zoom).",
                _ => string.Empty
            };

            bool isComms = _category == AudioCategory.OutputCommunications || _category == AudioCategory.InputCommunications;
            ChkMatchSound.Visibility = isComms ? Visibility.Visible : Visibility.Collapsed;

            _isUpdatingUi = true;
            try
            {
                ChkAutoSwitch.IsChecked = SettingsService.Instance.IsCategoryAutoSwitchEnabled(_category);
                ChkMatchSound.IsChecked = SettingsService.Instance.IsCategoryMirrored(_category);
            }
            finally
            {
                _isUpdatingUi = false;
            }

            ReloadData();
        }

        public void ReloadData()
        {
            Dispatcher.Invoke(() =>
            {
                bool isMirrored = SettingsService.Instance.IsCategoryMirrored(_category);
                
                _isUpdatingUi = true;
                try
                {
                    ChkMatchSound.IsChecked = isMirrored;
                    BannerMirrored.Visibility = isMirrored ? Visibility.Visible : Visibility.Collapsed;
                    if (isMirrored)
                    {
                        TxtBannerMirrored.Text = _category == AudioCategory.OutputCommunications
                            ? "Mirrored with Output (Sound) — Voice calls automatically use the same device as system sounds."
                            : "Mirrored with Input (Sound) — Voice calls automatically use the same microphone as system sounds.";
                    }
                }
                finally
                {
                    _isUpdatingUi = false;
                }

                var priorities = SettingsService.Instance.GetPriorities(_category);
                var devices = AudioDeviceManager.Instance.GetDevices(_category.GetDataFlow());
                string filter = TxtSearchFilter?.Text?.Trim() ?? string.Empty;

                _items.Clear();
                int rank = 1;

                foreach (var p in priorities)
                {
                    if (SettingsService.Instance.IsDeviceIgnored(p.Id, p.Name))
                        continue;

                    var dev = devices.FirstOrDefault(d => string.Equals(d.Id, p.Id, StringComparison.OrdinalIgnoreCase))
                           ?? devices.FirstOrDefault(d => string.Equals(d.Name, p.Name, StringComparison.OrdinalIgnoreCase));

                    bool isConnected = dev != null && dev.IsActive;
                    bool isDefault = dev != null && dev.IsDefault(_category);

                    int currentRank = rank++;

                    // Apply search filter if present
                    if (!string.IsNullOrEmpty(filter) &&
                        !p.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    _items.Add(new PriorityItemViewModel
                    {
                        Rank = currentRank,
                        Id = p.Id,
                        Name = p.Name,
                        IsConnected = isConnected,
                        StatusText = isConnected ? "Connected" : (dev != null ? dev.StatusText : "Offline / Unplugged"),
                        IsCurrentDefault = isDefault
                    });
                }

                if (_items.Count == 0)
                {
                    EmptyStatePanel.Visibility = Visibility.Visible;
                    if (!string.IsNullOrEmpty(filter))
                    {
                        TxtEmptyTitle.Text = "No devices match the filter";
                        TxtEmptySubtitle.Text = $"No prioritized devices containing '{filter}'";
                    }
                    else
                    {
                        TxtEmptyTitle.Text = "No prioritized devices set";
                        TxtEmptySubtitle.Text = "Select a device below and click '+ Add to Priority List'";
                    }
                }
                else
                {
                    EmptyStatePanel.Visibility = Visibility.Collapsed;
                }

                // Refresh Available Devices combo box
                CmbAvailableDevices.Items.Clear();
                foreach (var d in devices)
                {
                    if (SettingsService.Instance.IsDeviceIgnored(d.Id, d.Name))
                        continue;

                    bool inList = priorities.Any(p => string.Equals(p.Id, d.Id, StringComparison.OrdinalIgnoreCase)
                                                   || string.Equals(p.Name, d.Name, StringComparison.OrdinalIgnoreCase));
                    if (!inList)
                    {
                        CmbAvailableDevices.Items.Add(new ComboBoxItem
                        {
                            Content = $"{d.Name} ({d.StatusText})",
                            Tag = d
                        });
                    }
                }

                if (CmbAvailableDevices.Items.Count > 0)
                {
                    CmbAvailableDevices.SelectedIndex = 0;
                    BtnAddDevice.IsEnabled = true;
                    BtnHideDevice.IsEnabled = true;
                }
                else
                {
                    CmbAvailableDevices.Items.Add(new ComboBoxItem
                    {
                        Content = "(All detected devices are in list or hidden)",
                        IsEnabled = false
                    });
                    CmbAvailableDevices.SelectedIndex = 0;
                    BtnAddDevice.IsEnabled = false;
                    BtnHideDevice.IsEnabled = false;
                }
            });
        }

        private void TxtSearchFilter_TextChanged(object sender, TextChangedEventArgs e)
        {
            ReloadData();
        }

        private void BtnClearFilter_Click(object sender, RoutedEventArgs e)
        {
            TxtSearchFilter.Text = string.Empty;
        }

        private void BtnMoveUp_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is PriorityItemViewModel vm)
            {
                var list = SettingsService.Instance.GetPriorities(_category);
                int index = list.FindIndex(p => string.Equals(p.Id, vm.Id, StringComparison.OrdinalIgnoreCase)
                                             || string.Equals(p.Name, vm.Name, StringComparison.OrdinalIgnoreCase));
                if (index > 0)
                {
                    var item = list[index];
                    list.RemoveAt(index);
                    list.Insert(index - 1, item);
                    SettingsService.Instance.Save();
                    PrioritySwitcherService.Instance.EvaluateCategory(_category);
                    ReloadData();
                }
            }
        }

        private void BtnMoveDown_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is PriorityItemViewModel vm)
            {
                var list = SettingsService.Instance.GetPriorities(_category);
                int index = list.FindIndex(p => string.Equals(p.Id, vm.Id, StringComparison.OrdinalIgnoreCase)
                                             || string.Equals(p.Name, vm.Name, StringComparison.OrdinalIgnoreCase));
                if (index >= 0 && index < list.Count - 1)
                {
                    var item = list[index];
                    list.RemoveAt(index);
                    list.Insert(index + 1, item);
                    SettingsService.Instance.Save();
                    PrioritySwitcherService.Instance.EvaluateCategory(_category);
                    ReloadData();
                }
            }
        }

        private void BtnRemove_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is PriorityItemViewModel vm)
            {
                var list = SettingsService.Instance.GetPriorities(_category);
                list.RemoveAll(p => string.Equals(p.Id, vm.Id, StringComparison.OrdinalIgnoreCase)
                                 || string.Equals(p.Name, vm.Name, StringComparison.OrdinalIgnoreCase));
                SettingsService.Instance.Save();
                PrioritySwitcherService.Instance.EvaluateCategory(_category);
                ReloadData();
            }
        }

        private void BtnHideItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is PriorityItemViewModel vm)
            {
                SettingsService.Instance.IgnoreDevice(vm.Id, vm.Name);
                PrioritySwitcherService.Instance.EvaluateCategory(_category);
                ReloadData();
            }
        }

        private void BtnHideDevice_Click(object sender, RoutedEventArgs e)
        {
            if (CmbAvailableDevices.SelectedItem is ComboBoxItem item && item.Tag is AudioDevice device)
            {
                SettingsService.Instance.IgnoreDevice(device.Id, device.Name);
                PrioritySwitcherService.Instance.EvaluateCategory(_category);
                ReloadData();
            }
        }

        private void BtnSetDefault_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is PriorityItemViewModel vm)
            {
                AudioDeviceManager.Instance.SetDefaultDevice(vm.Id, _category);
                ReloadData();
            }
        }

        private void BtnAddDevice_Click(object sender, RoutedEventArgs e)
        {
            if (CmbAvailableDevices.SelectedItem is ComboBoxItem item && item.Tag is AudioDevice device)
            {
                var priorities = SettingsService.Instance.GetPriorities(_category);
                priorities.Add(new PriorityDeviceEntry(device.Id, device.Name));
                SettingsService.Instance.Save();

                ReloadData();
                PrioritySwitcherService.Instance.EvaluateCategory(_category);
            }
        }

        private void ChkAutoSwitch_Changed(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingUi) return;

            bool enabled = ChkAutoSwitch.IsChecked == true;
            SettingsService.Instance.SetCategoryAutoSwitchEnabled(_category, enabled);
            if (enabled)
            {
                PrioritySwitcherService.Instance.EvaluateCategory(_category);
            }
        }

        private void ChkMatchSound_Changed(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingUi) return;

            bool mirrored = ChkMatchSound.IsChecked == true;
            SettingsService.Instance.SetCategoryMirrored(_category, mirrored);
            PrioritySwitcherService.Instance.EvaluateAllPriorities();
            ReloadData();
        }
    }
}
