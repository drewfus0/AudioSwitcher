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
        private int _isEvaluating = 0;

        // Temporary overrides per category (category -> deviceId)
        private readonly Dictionary<AudioCategory, string> _temporaryOverrides = new();
        private readonly object _overrideLock = new();

        public event Action<AudioCategory, AudioDevice>? DeviceAutoSwitched;
        public event Action<AudioCategory, string?>? TemporaryOverrideChanged;

        public PrioritySwitcherService()
        {
            _deviceManager = AudioDeviceManager.Instance;
            _settingsService = SettingsService.Instance;

            _deviceManager.DevicesUpdated += OnDevicesUpdated;
            _settingsService.SettingsChanged += OnSettingsChanged;

            // Initial check
            EvaluateAllPriorities();
        }

        private void OnDevicesUpdated()
        {
            EvaluateAllPriorities();
        }

        private void OnSettingsChanged()
        {
            EvaluateAllPriorities();
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

            _deviceManager.SetDefaultDevice(deviceId, category);
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

            foreach (var entry in priorities)
            {
                // Match by ID first
                var match = activeDevices.FirstOrDefault(d => string.Equals(d.Id, entry.Id, StringComparison.OrdinalIgnoreCase));
                
                // If not found by ID, fallback to friendly name match (handles USB port changes)
                if (match == null)
                {
                    match = activeDevices.FirstOrDefault(d => string.Equals(d.Name, entry.Name, StringComparison.OrdinalIgnoreCase));
                }

                if (match != null)
                {
                    targetDevice = match;
                    break;
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
            if (!_settingsService.Settings.EnableVolumeMappingOnSwitch)
                return;

            if (category.GetDataFlow() != EDataFlow.eRender)
                return;

            try
            {
                var currentDefault = _deviceManager.GetDefaultDevice(category);
                if (currentDefault != null && !string.Equals(currentDefault.Id, targetDeviceId, StringComparison.OrdinalIgnoreCase))
                {
                    var targetDev = _deviceManager.GetDeviceById(targetDeviceId);
                    float currentVol = AudioVolumeManager.Instance.GetVolume(currentDefault.Id);
                    double mappedVol = VolumeMappingService.Instance.MapVolumeBetweenDevices(
                        currentDefault.Id, currentDefault.Name,
                        targetDeviceId, targetDev?.Name ?? targetDeviceId,
                        currentVol);

                    AudioVolumeManager.Instance.SetVolume(targetDeviceId, (float)mappedVol);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Volume mapping calculation error: {ex.Message}");
            }
        }
    }
}
