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

            ChkAutoSwitch.IsChecked = SettingsService.Instance.IsCategoryAutoSwitchEnabled(_category);

            ReloadData();
        }

        public void ReloadData()
        {
            Dispatcher.Invoke(() =>
            {
                var priorities = SettingsService.Instance.GetPriorities(_category);
                var devices = AudioDeviceManager.Instance.GetDevices(_category.GetDataFlow());

                _items.Clear();
                int rank = 1;

                foreach (var p in priorities)
                {
                    var dev = devices.FirstOrDefault(d => string.Equals(d.Id, p.Id, StringComparison.OrdinalIgnoreCase))
                           ?? devices.FirstOrDefault(d => string.Equals(d.Name, p.Name, StringComparison.OrdinalIgnoreCase));

                    bool isConnected = dev != null && dev.IsActive;
                    bool isDefault = dev != null && dev.IsDefault(_category);

                    _items.Add(new PriorityItemViewModel
                    {
                        Rank = rank++,
                        Id = p.Id,
                        Name = p.Name,
                        IsConnected = isConnected,
                        StatusText = isConnected ? "Connected" : (dev != null ? dev.StatusText : "Offline / Unplugged"),
                        IsCurrentDefault = isDefault
                    });
                }

                EmptyStatePanel.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

                // Refresh Available Devices combo box
                CmbAvailableDevices.Items.Clear();
                foreach (var d in devices)
                {
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
                }
                else
                {
                    CmbAvailableDevices.Items.Add(new ComboBoxItem
                    {
                        Content = "(All detected devices are already in priority list)",
                        IsEnabled = false
                    });
                    CmbAvailableDevices.SelectedIndex = 0;
                    BtnAddDevice.IsEnabled = false;
                }
            });
        }

        private void SavePriorities()
        {
            var list = SettingsService.Instance.GetPriorities(_category);
            list.Clear();
            foreach (var item in _items)
            {
                list.Add(new PriorityDeviceEntry(item.Id, item.Name));
            }
            SettingsService.Instance.Save();
            PrioritySwitcherService.Instance.EvaluateCategory(_category);
        }

        private void BtnMoveUp_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is PriorityItemViewModel vm)
            {
                int index = _items.IndexOf(vm);
                if (index > 0)
                {
                    _items.Move(index, index - 1);
                    UpdateRanks();
                    SavePriorities();
                }
            }
        }

        private void BtnMoveDown_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is PriorityItemViewModel vm)
            {
                int index = _items.IndexOf(vm);
                if (index < _items.Count - 1 && index >= 0)
                {
                    _items.Move(index, index + 1);
                    UpdateRanks();
                    SavePriorities();
                }
            }
        }

        private void BtnRemove_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is PriorityItemViewModel vm)
            {
                _items.Remove(vm);
                UpdateRanks();
                SavePriorities();
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
            bool enabled = ChkAutoSwitch.IsChecked == true;
            SettingsService.Instance.SetCategoryAutoSwitchEnabled(_category, enabled);
            if (enabled)
            {
                PrioritySwitcherService.Instance.EvaluateCategory(_category);
            }
        }

        private void UpdateRanks()
        {
            for (int i = 0; i < _items.Count; i++)
            {
                _items[i].Rank = i + 1;
            }
        }
    }
}
