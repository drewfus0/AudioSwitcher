using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AudioSwitcher.CoreAudio;
using AudioSwitcher.Services;
using WpfPoint = System.Windows.Point;
using WpfVector = System.Windows.Vector;

namespace AudioSwitcher.UI
{
    public partial class PriorityListControl : UserControl
    {
        private AudioCategory _category;
        private readonly ObservableCollection<PriorityItemViewModel> _items = new();
        private bool _isUpdatingUi = false;

        // Drag & Drop State
        private WpfPoint _dragStartPoint;
        private PriorityItemViewModel? _draggedItem;
        private bool _isDragging = false;

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

                // Check Temporary Override for this category
                string? tempOverrideId = PrioritySwitcherService.Instance.GetTemporaryOverride(_category);
                bool hasTempOverride = !string.IsNullOrEmpty(tempOverrideId);

                var priorities = SettingsService.Instance.GetPriorities(_category);
                var devices = AudioDeviceManager.Instance.GetDevices(_category.GetDataFlow());
                string filter = TxtSearchFilter?.Text?.Trim() ?? string.Empty;

                if (hasTempOverride)
                {
                    var tempDev = devices.FirstOrDefault(d => string.Equals(d.Id, tempOverrideId, StringComparison.OrdinalIgnoreCase))
                               ?? devices.FirstOrDefault(d => string.Equals(d.Name, tempOverrideId, StringComparison.OrdinalIgnoreCase));
                    string tempName = tempDev?.Name ?? "Selected Device";
                    TxtTempOverrideDetails.Text = $"Using \"{tempName}\". Priority auto-switching is paused until cleared or disconnected.";
                    BannerTempOverride.Visibility = Visibility.Visible;
                }
                else
                {
                    BannerTempOverride.Visibility = Visibility.Collapsed;
                }

                // Cross-category default checking
                AudioCategory otherCat = _category switch
                {
                    AudioCategory.OutputSound => AudioCategory.OutputCommunications,
                    AudioCategory.OutputCommunications => AudioCategory.OutputSound,
                    AudioCategory.InputSound => AudioCategory.InputCommunications,
                    AudioCategory.InputCommunications => AudioCategory.InputSound,
                    _ => _category
                };
                var otherDefaultDev = AudioDeviceManager.Instance.GetDefaultDevice(otherCat);

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
                    bool isThisTempOverride = hasTempOverride && (
                        string.Equals(p.Id, tempOverrideId, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(p.Name, tempOverrideId, StringComparison.OrdinalIgnoreCase));

                    // Check if default in another category
                    string otherBadge = string.Empty;
                    if (otherDefaultDev != null && dev != null && (
                        string.Equals(dev.Id, otherDefaultDev.Id, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(dev.Name, otherDefaultDev.Name, StringComparison.OrdinalIgnoreCase)))
                    {
                        otherBadge = otherCat switch
                        {
                            AudioCategory.OutputSound => "🔊 Sound Default",
                            AudioCategory.OutputCommunications => "🎧 Comms Default",
                            AudioCategory.InputSound => "🎤 Sound Mic",
                            AudioCategory.InputCommunications => "🎙️ Comms Mic",
                            _ => string.Empty
                        };
                    }

                    int currentRank = rank++;

                    // Query volume status if connected and render
                    int volPercent = 100;
                    bool isMuted = false;
                    bool hasVolumeControl = isConnected && _category.GetDataFlow() == EDataFlow.eRender;

                    if (isConnected && dev != null)
                    {
                        volPercent = AudioVolumeManager.Instance.GetVolumePercent(dev.Id);
                        isMuted = AudioVolumeManager.Instance.GetMute(dev.Id);
                    }

                    _items.Add(new PriorityItemViewModel
                    {
                        Rank = currentRank,
                        Id = p.Id,
                        Name = p.Name,
                        IsConnected = isConnected,
                        StatusText = isConnected ? "Connected" : (dev != null ? dev.StatusText : "Offline / Unplugged"),
                        IsCurrentDefault = isDefault,
                        IsTemporaryOverride = isThisTempOverride,
                        OtherCategoryBadge = otherBadge,
                        VolumePercent = volPercent,
                        IsMuted = isMuted,
                        HasVolumeControl = hasVolumeControl
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

        #region Drag and Drop Reordering

        private void DragHandle_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(this);
            if (sender is FrameworkElement fe && fe.DataContext is PriorityItemViewModel vm)
            {
                _draggedItem = vm;
            }
        }

        private void DragHandle_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && _draggedItem != null && !_isDragging)
            {
                WpfPoint currentPoint = e.GetPosition(this);
                WpfVector diff = _dragStartPoint - currentPoint;

                if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    _isDragging = true;
                    try
                    {
                        if (sender is DependencyObject dragSource)
                        {
                            DragDrop.DoDragDrop(dragSource, _draggedItem, DragDropEffects.Move);
                        }
                    }
                    finally
                    {
                        _isDragging = false;
                        _draggedItem = null;
                        ClearAllDropGuides();
                    }
                }
            }
        }

        private void DragHandle_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _draggedItem = null;
            _isDragging = false;
            ClearAllDropGuides();
        }

        private void ItemCard_DragOver(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(typeof(PriorityItemViewModel)))
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }

            e.Effects = DragDropEffects.Move;
            e.Handled = true;

            if (sender is FrameworkElement fe)
            {
                WpfPoint pos = e.GetPosition(fe);
                double height = fe.ActualHeight;
                bool dropBefore = pos.Y < (height / 2.0);

                var (topGuide, bottomGuide) = GetDropGuides(fe);
                if (topGuide != null && bottomGuide != null)
                {
                    topGuide.Visibility = dropBefore ? Visibility.Visible : Visibility.Collapsed;
                    bottomGuide.Visibility = dropBefore ? Visibility.Collapsed : Visibility.Visible;
                }
            }
        }

        private void ItemCard_DragLeave(object sender, DragEventArgs e)
        {
            if (sender is FrameworkElement fe)
            {
                var (topGuide, bottomGuide) = GetDropGuides(fe);
                if (topGuide != null) topGuide.Visibility = Visibility.Collapsed;
                if (bottomGuide != null) bottomGuide.Visibility = Visibility.Collapsed;
            }
        }

        private void ItemCard_Drop(object sender, DragEventArgs e)
        {
            ClearAllDropGuides();

            if (!e.Data.GetDataPresent(typeof(PriorityItemViewModel)))
                return;

            var sourceItem = e.Data.GetData(typeof(PriorityItemViewModel)) as PriorityItemViewModel;
            var targetItem = (sender as FrameworkElement)?.DataContext as PriorityItemViewModel;

            if (sourceItem == null || targetItem == null || string.Equals(sourceItem.Id, targetItem.Id, StringComparison.OrdinalIgnoreCase))
                return;

            if (sender is FrameworkElement fe)
            {
                WpfPoint pos = e.GetPosition(fe);
                double height = fe.ActualHeight;
                bool dropBefore = pos.Y < (height / 2.0);

                var list = SettingsService.Instance.GetPriorities(_category);
                int sourceIdx = list.FindIndex(p => string.Equals(p.Id, sourceItem.Id, StringComparison.OrdinalIgnoreCase)
                                                 || string.Equals(p.Name, sourceItem.Name, StringComparison.OrdinalIgnoreCase));
                int targetIdx = list.FindIndex(p => string.Equals(p.Id, targetItem.Id, StringComparison.OrdinalIgnoreCase)
                                                 || string.Equals(p.Name, targetItem.Name, StringComparison.OrdinalIgnoreCase));

                if (sourceIdx >= 0 && targetIdx >= 0)
                {
                    var entry = list[sourceIdx];
                    list.RemoveAt(sourceIdx);

                    int newIdx = targetIdx;
                    if (sourceIdx < targetIdx)
                    {
                        newIdx = dropBefore ? targetIdx - 1 : targetIdx;
                    }
                    else
                    {
                        newIdx = dropBefore ? targetIdx : targetIdx + 1;
                    }

                    if (newIdx < 0) newIdx = 0;
                    if (newIdx > list.Count) newIdx = list.Count;

                    list.Insert(newIdx, entry);
                    SettingsService.Instance.Save();

                    if (!PrioritySwitcherService.Instance.HasTemporaryOverride(_category))
                    {
                        PrioritySwitcherService.Instance.EvaluateCategory(_category);
                    }
                    ReloadData();
                }
            }
        }

        private (Border? topGuide, Border? bottomGuide) GetDropGuides(FrameworkElement element)
        {
            if (element is Border bdr && bdr.Child is Grid grid)
            {
                var top = grid.Children.OfType<Border>().FirstOrDefault(b => b.Name == "TopDropGuide");
                var bottom = grid.Children.OfType<Border>().FirstOrDefault(b => b.Name == "BottomDropGuide");
                return (top, bottom);
            }
            return (null, null);
        }

        private void ClearAllDropGuides()
        {
            for (int i = 0; i < ItemsPriorityList.Items.Count; i++)
            {
                var container = ItemsPriorityList.ItemContainerGenerator.ContainerFromIndex(i) as FrameworkElement;
                if (container != null)
                {
                    var border = FindVisualChild<Border>(container, "CardBorder");
                    if (border != null)
                    {
                        var (top, bottom) = GetDropGuides(border);
                        if (top != null) top.Visibility = Visibility.Collapsed;
                        if (bottom != null) bottom.Visibility = Visibility.Collapsed;
                    }
                }
            }
        }

        private static T? FindVisualChild<T>(DependencyObject parent, string name) where T : FrameworkElement
        {
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typedChild && typedChild.Name == name)
                    return typedChild;

                var result = FindVisualChild<T>(child, name);
                if (result != null)
                    return result;
            }
            return null;
        }

        private static bool IsInteractiveControl(DependencyObject? source)
        {
            while (source != null)
            {
                if (source is System.Windows.Controls.Primitives.ButtonBase ||
                    source is System.Windows.Controls.TextBox ||
                    source is System.Windows.Controls.ComboBox ||
                    source is System.Windows.Controls.CheckBox)
                {
                    return true;
                }
                source = VisualTreeHelper.GetParent(source);
            }
            return false;
        }

        #endregion

        #region Actions & Event Handlers

        private void TxtSearchFilter_TextChanged(object sender, TextChangedEventArgs e)
        {
            ReloadData();
        }

        private void BtnClearFilter_Click(object sender, RoutedEventArgs e)
        {
            TxtSearchFilter.Text = string.Empty;
        }

        private void BtnTempSwitch_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is PriorityItemViewModel vm && vm.IsConnected)
            {
                PrioritySwitcherService.Instance.SetTemporaryOverride(_category, vm.Id);
                ReloadData();
            }
        }

        private void BtnClearTempOverride_Click(object sender, RoutedEventArgs e)
        {
            PrioritySwitcherService.Instance.ClearTemporaryOverride(_category);
            ReloadData();
        }

        private void BtnMakeTempPermanent_Click(object sender, RoutedEventArgs e)
        {
            string? tempId = PrioritySwitcherService.Instance.GetTemporaryOverride(_category);
            if (!string.IsNullOrEmpty(tempId))
            {
                var list = SettingsService.Instance.GetPriorities(_category);
                int index = list.FindIndex(p => string.Equals(p.Id, tempId, StringComparison.OrdinalIgnoreCase)
                                             || string.Equals(p.Name, tempId, StringComparison.OrdinalIgnoreCase));
                if (index >= 0)
                {
                    var entry = list[index];
                    list.RemoveAt(index);
                    list.Insert(0, entry);
                }
                else
                {
                    var dev = AudioDeviceManager.Instance.GetDeviceById(tempId);
                    if (dev != null)
                    {
                        list.Insert(0, new PriorityDeviceEntry(dev.Id, dev.Name));
                    }
                }

                SettingsService.Instance.Save();
                PrioritySwitcherService.Instance.ClearTemporaryOverride(_category);
                ReloadData();
            }
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
                    if (!PrioritySwitcherService.Instance.HasTemporaryOverride(_category))
                    {
                        PrioritySwitcherService.Instance.EvaluateCategory(_category);
                    }
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
                    if (!PrioritySwitcherService.Instance.HasTemporaryOverride(_category))
                    {
                        PrioritySwitcherService.Instance.EvaluateCategory(_category);
                    }
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

        private void BtnAddDevice_Click(object sender, RoutedEventArgs e)
        {
            if (CmbAvailableDevices.SelectedItem is ComboBoxItem item && item.Tag is AudioDevice device)
            {
                var priorities = SettingsService.Instance.GetPriorities(_category);
                priorities.Add(new PriorityDeviceEntry(device.Id, device.Name));
                SettingsService.Instance.Save();

                ReloadData();
                if (!PrioritySwitcherService.Instance.HasTemporaryOverride(_category))
                {
                    PrioritySwitcherService.Instance.EvaluateCategory(_category);
                }
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

        private void DeviceVolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isUpdatingUi) return;
            if (sender is Slider slider && slider.Tag is PriorityItemViewModel vm && vm.IsConnected)
            {
                int newPercent = (int)Math.Round(slider.Value);
                AudioVolumeManager.Instance.SetVolumePercent(vm.Id, newPercent);
                vm.VolumePercent = newPercent;
            }
        }

        private void BtnMuteDevice_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is PriorityItemViewModel vm && vm.IsConnected)
            {
                bool newMute = !vm.IsMuted;
                AudioVolumeManager.Instance.SetMute(vm.Id, newMute);
                vm.IsMuted = newMute;
            }
        }

        private void BtnOpenCalibrationLab_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var win = new VolumeCalibrationWindow();
                var parentWin = Window.GetWindow(this);
                if (parentWin != null && parentWin.IsLoaded && parentWin.IsVisible)
                {
                    win.Owner = parentWin;
                }
                win.ShowDialog();
                ReloadData();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error launching VolumeCalibrationWindow: {ex}");
                MessageBox.Show($"Unable to open Volume Calibration Lab:\n{ex.Message}", "AudioSwitcher", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        #endregion
    }
}
