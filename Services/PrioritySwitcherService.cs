using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AudioSwitcher.CoreAudio;

namespace AudioSwitcher.Services
{
    public class PrioritySwitcherService
    {
        private static PrioritySwitcherService? _instance;
        public static PrioritySwitcherService Instance => _instance ??= new PrioritySwitcherService();

        private readonly AudioDeviceManager _deviceManager;
        private readonly SettingsService _settingsService;
        private readonly System.Threading.Timer _debounceTimer;
        private int _isEvaluating = 0;

        // Active output tracking for reliable volume auto-leveling
        private string? _lastActiveOutputDeviceId;
        private string? _lastActiveOutputDeviceName;
        private float _lastActiveOutputVolume = 0.5f;

        // Temporary overrides per category (category -> deviceId)
        private readonly Dictionary<AudioCategory, string> _temporaryOverrides = new();
        private readonly object _overrideLock = new();

        public event Action<AudioCategory, AudioDevice>? DeviceAutoSwitched;
        public event Action<AudioCategory, string?>? TemporaryOverrideChanged;

        public PrioritySwitcherService()
        {
            _deviceManager = AudioDeviceManager.Instance;
            _settingsService = SettingsService.Instance;

            _debounceTimer = new System.Threading.Timer(_ =>
            {
                EvaluateAllPrioritiesInternal();
            }, null, Timeout.Infinite, Timeout.Infinite);

            // Seed initial active output
            var initialDefault = _deviceManager.GetDefaultDevice(AudioCategory.OutputSound);
            if (initialDefault != null)
            {
                _lastActiveOutputDeviceId = initialDefault.Id;
                _lastActiveOutputDeviceName = initialDefault.Name;
                _lastActiveOutputVolume = AudioVolumeManager.Instance.GetVolume(initialDefault.Id);
            }

            AudioVolumeManager.Instance.VolumeChanged += (devId, vol, mute) =>
            {
                if (string.Equals(devId, _lastActiveOutputDeviceId, StringComparison.OrdinalIgnoreCase))
                {
                    _lastActiveOutputVolume = vol;
                }
            };

            _deviceManager.DevicesUpdated += OnDevicesUpdated;
            _settingsService.SettingsChanged += OnSettingsChanged;

            // Initial check
            EvaluateAllPriorities();
        }

        private void OnDevicesUpdated()
        {
            // Debounce by 250ms to allow Bluetooth link / hardware USB transients to settle
            _debounceTimer.Change(250, Timeout.Infinite);
        }

        private void OnSettingsChanged()
        {
            _debounceTimer.Change(50, Timeout.Infinite);
        }

        public bool HasTemporaryOverride(AudioCategory category)
        {
            lock (_overrideLock)
            {
                return _temporaryOverrides.ContainsKey(category);
            }
        }

        public string? GetTemporaryOverride(AudioCategory category)
        {
            lock (_overrideLock)
            {
                return _temporaryOverrides.TryGetValue(category, out var id) ? id : null;
            }
        }

        public void SetTemporaryOverride(AudioCategory category, string deviceId)
        {
            // Apply volume mapping from current default if applicable
            ApplyVolumeMappingIfEnabled(category, deviceId);

            lock (_overrideLock)
            {
                _temporaryOverrides[category] = deviceId;
                if (_settingsService.IsCategoryMirrored(category))
                {
                    if (category == AudioCategory.OutputSound)
                        _temporaryOverrides[AudioCategory.OutputCommunications] = deviceId;
                    else if (category == AudioCategory.InputSound)
                        _temporaryOverrides[AudioCategory.InputCommunications] = deviceId;
                }
            }

            bool success = _deviceManager.SetDefaultDevice(deviceId, category);
            var dev = _deviceManager.GetDeviceById(deviceId);
            if (dev != null && success)
            {
                DeviceAutoSwitched?.Invoke(category, dev);
            }

            TemporaryOverrideChanged?.Invoke(category, deviceId);
            if (_settingsService.IsCategoryMirrored(category))
            {
                if (category == AudioCategory.OutputSound)
                    TemporaryOverrideChanged?.Invoke(AudioCategory.OutputCommunications, deviceId);
                else if (category == AudioCategory.InputSound)
                    TemporaryOverrideChanged?.Invoke(AudioCategory.InputCommunications, deviceId);
            }
        }

        public void ClearTemporaryOverride(AudioCategory category)
        {
            bool removed = false;
            lock (_overrideLock)
            {
                if (_temporaryOverrides.Remove(category))
                {
                    removed = true;
                }
                if (_settingsService.IsCategoryMirrored(category))
                {
                    if (category == AudioCategory.OutputSound && _temporaryOverrides.Remove(AudioCategory.OutputCommunications))
                        removed = true;
                    else if (category == AudioCategory.InputSound && _temporaryOverrides.Remove(AudioCategory.InputCommunications))
                        removed = true;
                }
            }

            if (removed)
            {
                TemporaryOverrideChanged?.Invoke(category, null);
                if (_settingsService.IsCategoryMirrored(category))
                {
                    if (category == AudioCategory.OutputSound)
                        TemporaryOverrideChanged?.Invoke(AudioCategory.OutputCommunications, null);
                    else if (category == AudioCategory.InputSound)
                        TemporaryOverrideChanged?.Invoke(AudioCategory.InputCommunications, null);
                }

                EvaluateCategory(category);
                if (_settingsService.IsCategoryMirrored(category))
                {
                    if (category == AudioCategory.OutputSound)
                        EvaluateCategory(AudioCategory.OutputCommunications);
                    else if (category == AudioCategory.InputSound)
                        EvaluateCategory(AudioCategory.InputCommunications);
                }
            }
        }

        public void ClearAllTemporaryOverrides()
        {
            lock (_overrideLock)
            {
                _temporaryOverrides.Clear();
            }

            TemporaryOverrideChanged?.Invoke(AudioCategory.OutputSound, null);
            TemporaryOverrideChanged?.Invoke(AudioCategory.OutputCommunications, null);
            TemporaryOverrideChanged?.Invoke(AudioCategory.InputSound, null);
            TemporaryOverrideChanged?.Invoke(AudioCategory.InputCommunications, null);

            EvaluateAllPriorities();
        }

        public void EvaluateAllPriorities()
        {
            _debounceTimer.Change(0, Timeout.Infinite);
        }

        private void EvaluateAllPrioritiesInternal()
        {
            if (Interlocked.CompareExchange(ref _isEvaluating, 1, 0) != 0)
                return;

            Task.Run(() =>
            {
                try
                {
                    EvaluateCategory(AudioCategory.OutputSound);
                    EvaluateCategory(AudioCategory.OutputCommunications);
                    EvaluateCategory(AudioCategory.InputSound);
                    EvaluateCategory(AudioCategory.InputCommunications);
                }
                finally
                {
                    Interlocked.Exchange(ref _isEvaluating, 0);
                }
            });
        }

        public void EvaluateCategory(AudioCategory category)
        {
            if (!_settingsService.IsCategoryAutoSwitchEnabled(category))
                return;

            var flow = category.GetDataFlow();
            var activeDevices = _deviceManager.GetDevices(flow)
                .Where(d => d.IsActive && !_settingsService.IsDeviceIgnored(d.Id, d.Name))
                .ToList();

            if (activeDevices.Count == 0)
                return;

            // Check if temporary override is active for this category
            string? overrideDeviceId = null;
            lock (_overrideLock)
            {
                _temporaryOverrides.TryGetValue(category, out overrideDeviceId);
            }

            if (!string.IsNullOrEmpty(overrideDeviceId))
            {
                var overrideDev = activeDevices.FirstOrDefault(d => string.Equals(d.Id, overrideDeviceId, StringComparison.OrdinalIgnoreCase))
                               ?? activeDevices.FirstOrDefault(d => string.Equals(d.Name, overrideDeviceId, StringComparison.OrdinalIgnoreCase));

                if (overrideDev != null)
                {
                    // Override device is connected and valid: retain as default
                    if (!overrideDev.IsDefault(category))
                    {
                        ApplyVolumeMappingIfEnabled(category, overrideDev.Id);
                        bool success = _deviceManager.SetDefaultDevice(overrideDev.Id, category);
                        if (success)
                        {
                            DeviceAutoSwitched?.Invoke(category, overrideDev);
                        }
                    }
                    return;
                }
                else
                {
                    // The temporary override device was disconnected/unplugged! Automatically clear override
                    lock (_overrideLock)
                    {
                        _temporaryOverrides.Remove(category);
                    }
                    TemporaryOverrideChanged?.Invoke(category, null);
                }
            }

            var priorities = _settingsService.GetPriorities(category);
            if (priorities.Count == 0)
                return;

            // Find highest priority available device
            AudioDevice? targetDevice = null;
            int targetPriorityIndex = -1;

            for (int i = 0; i < priorities.Count; i++)
            {
                var entry = priorities[i];
                var match = activeDevices.FirstOrDefault(d => string.Equals(d.Id, entry.Id, StringComparison.OrdinalIgnoreCase))
                         ?? activeDevices.FirstOrDefault(d => string.Equals(d.Name, entry.Name, StringComparison.OrdinalIgnoreCase));

                if (match != null)
                {
                    targetDevice = match;
                    targetPriorityIndex = i;
                    break;
                }
            }

            // If a fallback device is matched but is not #1 in priority (e.g. index > 0),
            // wait briefly (grace period up to 3500ms) to allow higher-priority Bluetooth devices (which reconnect after USB unplug) to come online
            // instead of prematurely double-switching to an intermediate device.
            if (targetPriorityIndex > 0 && category == AudioCategory.OutputSound)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                while (sw.ElapsedMilliseconds < 3500)
                {
                    _deviceManager.RefreshDevices();
                    var refreshedActive = _deviceManager.GetDevices(flow)
                        .Where(d => d.IsActive && !_settingsService.IsDeviceIgnored(d.Id, d.Name))
                        .ToList();

                    bool foundHigher = false;
                    for (int i = 0; i < targetPriorityIndex; i++)
                    {
                        var higherEntry = priorities[i];
                        var higherMatch = refreshedActive.FirstOrDefault(d => string.Equals(d.Id, higherEntry.Id, StringComparison.OrdinalIgnoreCase))
                                       ?? refreshedActive.FirstOrDefault(d => string.Equals(d.Name, higherEntry.Name, StringComparison.OrdinalIgnoreCase));

                        if (higherMatch != null)
                        {
                            targetDevice = higherMatch;
                            targetPriorityIndex = i;
                            foundHigher = true;
                            break;
                        }
                    }

                    if (foundHigher && targetPriorityIndex == 0)
                    {
                        break;
                    }
                    if (foundHigher)
                    {
                        // Found a higher-priority device (like Bluetooth Qudelix), wait a tiny moment to ensure its endpoint is stable
                        Thread.Sleep(150);
                        break;
                    }

                    Thread.Sleep(200);
                }
            }

            if (targetDevice == null)
                return;

            // Check if it's already the default
            bool isAlreadyDefault = targetDevice.IsDefault(category);
            if (!isAlreadyDefault)
            {
                ApplyVolumeMappingIfEnabled(category, targetDevice.Id);
                bool success = _deviceManager.SetDefaultDevice(targetDevice.Id, category);
                if (success)
                {
                    DeviceAutoSwitched?.Invoke(category, targetDevice);
                }
            }
        }

        private void ApplyVolumeMappingIfEnabled(AudioCategory category, string targetDeviceId)
        {
            if (category.GetDataFlow() != EDataFlow.eRender)
                return;

            var targetDev = _deviceManager.GetDeviceById(targetDeviceId);
            if (targetDev == null)
                return;

            if (!_settingsService.Settings.EnableVolumeMappingOnSwitch)
            {
                // When volume mapping is disabled, restore the saved volume & mute state for this device
                int savedVol = _settingsService.GetSavedDeviceVolume(targetDev.Id, targetDev.Name, -1);
                if (savedVol >= 0)
                {
                    AudioVolumeManager.Instance.SetVolumePercent(targetDev.Id, savedVol);
                    _lastActiveOutputVolume = savedVol / 100.0f;
                }
                else
                {
                    _lastActiveOutputVolume = AudioVolumeManager.Instance.GetVolume(targetDev.Id);
                }

                _lastActiveOutputDeviceId = targetDev.Id;
                _lastActiveOutputDeviceName = targetDev.Name;
                return;
            }

            try
            {
                string fromId = _lastActiveOutputDeviceId ?? string.Empty;
                string fromName = _lastActiveOutputDeviceName ?? fromId;
                float fromVol = _lastActiveOutputVolume;

                // If fromId is empty or same as target, check current default
                if (string.IsNullOrWhiteSpace(fromId) || string.Equals(fromId, targetDeviceId, StringComparison.OrdinalIgnoreCase))
                {
                    var currentDefault = _deviceManager.GetDefaultDevice(category);
                    if (currentDefault != null && !string.Equals(currentDefault.Id, targetDeviceId, StringComparison.OrdinalIgnoreCase))
                    {
                        fromId = currentDefault.Id;
                        fromName = currentDefault.Name;
                        fromVol = AudioVolumeManager.Instance.GetLastKnownVolume(currentDefault.Id);
                    }
                }

                if (!string.IsNullOrWhiteSpace(fromId) && !string.Equals(fromId, targetDeviceId, StringComparison.OrdinalIgnoreCase))
                {
                    double mappedVol = VolumeMappingService.Instance.MapVolumeBetweenDevices(
                        fromId, fromName,
                        targetDev.Id, targetDev.Name,
                        fromVol);

                    AudioVolumeManager.Instance.SetVolume(targetDev.Id, (float)mappedVol);
                    _lastActiveOutputDeviceId = targetDev.Id;
                    _lastActiveOutputDeviceName = targetDev.Name;
                    _lastActiveOutputVolume = (float)mappedVol;
                }
                else
                {
                    // Same device reconnected: restore its saved volume
                    int savedVol = _settingsService.GetSavedDeviceVolume(targetDev.Id, targetDev.Name, -1);
                    if (savedVol >= 0)
                    {
                        AudioVolumeManager.Instance.SetVolumePercent(targetDev.Id, savedVol);
                        _lastActiveOutputVolume = savedVol / 100.0f;
                    }
                    else
                    {
                        _lastActiveOutputVolume = AudioVolumeManager.Instance.GetVolume(targetDev.Id);
                    }

                    _lastActiveOutputDeviceId = targetDev.Id;
                    _lastActiveOutputDeviceName = targetDev.Name;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Volume mapping calculation error: {ex.Message}");
            }
        }
    }
}
