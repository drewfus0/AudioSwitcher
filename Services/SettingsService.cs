using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using AudioSwitcher.CoreAudio;

namespace AudioSwitcher.Services
{
    public class PriorityDeviceEntry
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;

        public PriorityDeviceEntry() { }

        public PriorityDeviceEntry(string id, string name)
        {
            Id = id;
            Name = name;
        }
    }

    public class IgnoredDeviceEntry
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;

        public IgnoredDeviceEntry() { }

        public IgnoredDeviceEntry(string id, string name)
        {
            Id = id;
            Name = name;
        }
    }

    public class VolumeCalibrationPoint
    {
        public double RefVolume { get; set; }     // 0.0 to 1.0 (Master/Reference loudness)
        public double TargetVolume { get; set; }  // 0.0 to 1.0 (Hardware volume on this device)

        public VolumeCalibrationPoint() { }

        public VolumeCalibrationPoint(double refVolume, double targetVolume)
        {
            RefVolume = Math.Clamp(refVolume, 0.0, 1.0);
            TargetVolume = Math.Clamp(targetVolume, 0.0, 1.0);
        }
    }

    public class DeviceVolumeProfile
    {
        public string DeviceId { get; set; } = string.Empty;
        public string DeviceName { get; set; } = string.Empty;
        public List<VolumeCalibrationPoint> Points { get; set; } = new();

        public DeviceVolumeProfile() { }

        public DeviceVolumeProfile(string deviceId, string deviceName)
        {
            DeviceId = deviceId;
            DeviceName = deviceName;
            // Default 1:1 linear curve with 3 calibration points
            Points = new List<VolumeCalibrationPoint>
            {
                new(0.0, 0.0),
                new(0.25, 0.25),
                new(0.50, 0.50),
                new(0.75, 0.75),
                new(1.0, 1.0)
            };
        }
    }

    public class AppSettings
    {
        public List<PriorityDeviceEntry> OutputSoundPriorities { get; set; } = new();
        public List<PriorityDeviceEntry> OutputCommsPriorities { get; set; } = new();
        public List<PriorityDeviceEntry> InputSoundPriorities { get; set; } = new();
        public List<PriorityDeviceEntry> InputCommsPriorities { get; set; } = new();

        public List<IgnoredDeviceEntry> IgnoredDevices { get; set; } = new();

        public bool AutoSwitchEnabled { get; set; } = true;
        public bool AutoSwitchOutputSound { get; set; } = true;
        public bool AutoSwitchOutputComms { get; set; } = true;
        public bool AutoSwitchInputSound { get; set; } = true;
        public bool AutoSwitchInputComms { get; set; } = true;

        public bool MatchOutputCommsToSound { get; set; } = false;
        public bool MatchInputCommsToSound { get; set; } = false;

        public bool ShowSwitchNotifications { get; set; } = true;
        public bool StartWithWindows { get; set; } = false;
        public bool StartMinimized { get; set; } = true;
        public bool CloseToTray { get; set; } = true;

        // Volume Mapping & Normalization
        public bool EnableVolumeMappingOnSwitch { get; set; } = true;
        public string ReferenceDeviceId { get; set; } = string.Empty;
        public List<DeviceVolumeProfile> VolumeProfiles { get; set; } = new();

        // Persistent Device Volume & Mute Memory (survives unplugging, restarts, and USB port changes)
        public Dictionary<string, int> SavedDeviceVolumes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, bool> SavedDeviceMutes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public class SettingsService
    {
        private static SettingsService? _instance;
        public static SettingsService Instance => _instance ??= new SettingsService();

        private readonly string _settingsFolder;
        private readonly string _settingsFilePath;
        private readonly JsonSerializerOptions _jsonOptions;

        public AppSettings Settings { get; private set; }

        public event Action? SettingsChanged;

        public SettingsService()
        {
            _settingsFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AudioSwitcher");
            _settingsFilePath = Path.Combine(_settingsFolder, "settings.json");
            _jsonOptions = new JsonSerializerOptions { WriteIndented = true };

            Settings = LoadSettings();
        }

        public bool IsCategoryMirrored(AudioCategory category) => category switch
        {
            AudioCategory.OutputCommunications => Settings.MatchOutputCommsToSound,
            AudioCategory.InputCommunications => Settings.MatchInputCommsToSound,
            _ => false
        };

        public void SetCategoryMirrored(AudioCategory category, bool mirrored)
        {
            if (category == AudioCategory.OutputCommunications)
            {
                Settings.MatchOutputCommsToSound = mirrored;
            }
            else if (category == AudioCategory.InputCommunications)
            {
                Settings.MatchInputCommsToSound = mirrored;
            }
            Save();
        }

        public List<PriorityDeviceEntry> GetPriorities(AudioCategory category) => category switch
        {
            AudioCategory.OutputSound => Settings.OutputSoundPriorities,
            AudioCategory.OutputCommunications => Settings.MatchOutputCommsToSound ? Settings.OutputSoundPriorities : Settings.OutputCommsPriorities,
            AudioCategory.InputSound => Settings.InputSoundPriorities,
            AudioCategory.InputCommunications => Settings.MatchInputCommsToSound ? Settings.InputSoundPriorities : Settings.InputCommsPriorities,
            _ => Settings.OutputSoundPriorities
        };

        public bool IsCategoryAutoSwitchEnabled(AudioCategory category)
        {
            if (!Settings.AutoSwitchEnabled) return false;

            return category switch
            {
                AudioCategory.OutputSound => Settings.AutoSwitchOutputSound,
                AudioCategory.OutputCommunications => Settings.AutoSwitchOutputComms,
                AudioCategory.InputSound => Settings.AutoSwitchInputSound,
                AudioCategory.InputCommunications => Settings.AutoSwitchInputComms,
                _ => true
            };
        }

        public void SetCategoryAutoSwitchEnabled(AudioCategory category, bool enabled)
        {
            switch (category)
            {
                case AudioCategory.OutputSound:
                    Settings.AutoSwitchOutputSound = enabled;
                    break;
                case AudioCategory.OutputCommunications:
                    Settings.AutoSwitchOutputComms = enabled;
                    break;
                case AudioCategory.InputSound:
                    Settings.AutoSwitchInputSound = enabled;
                    break;
                case AudioCategory.InputCommunications:
                    Settings.AutoSwitchInputComms = enabled;
                    break;
            }
            Save();
        }

        public bool IsDeviceIgnored(string? id, string? name)
        {
            if (Settings.IgnoredDevices == null || Settings.IgnoredDevices.Count == 0)
                return false;

            return Settings.IgnoredDevices.Any(d =>
                (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(d.Id) && string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(d.Name) && string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase)));
        }

        public void IgnoreDevice(string id, string name)
        {
            if (string.IsNullOrWhiteSpace(id) && string.IsNullOrWhiteSpace(name))
                return;

            if (!IsDeviceIgnored(id, name))
            {
                Settings.IgnoredDevices.Add(new IgnoredDeviceEntry(id, name));
                RemoveDeviceFromAllPriorities(id, name);
                Save();
            }
        }

        public void UnignoreDevice(string id, string name)
        {
            Settings.IgnoredDevices.RemoveAll(d =>
                (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(d.Id) && string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(d.Name) && string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase)));
            Save();
        }

        public void UnignoreAllDevices()
        {
            Settings.IgnoredDevices.Clear();
            Save();
        }

        public void RemoveDeviceFromAllPriorities(string id, string name)
        {
            var categories = new[]
            {
                AudioCategory.OutputSound,
                AudioCategory.OutputCommunications,
                AudioCategory.InputSound,
                AudioCategory.InputCommunications
            };

            foreach (var cat in categories)
            {
                var list = GetPriorities(cat);
                list.RemoveAll(p =>
                    (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(p.Id) && string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(p.Name) && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)));
            }
        }

        public DeviceVolumeProfile GetOrCreateVolumeProfile(string deviceId, string deviceName)
        {
            var existing = Settings.VolumeProfiles.FirstOrDefault(p =>
                (!string.IsNullOrEmpty(deviceId) && string.Equals(p.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(deviceName) && string.Equals(p.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase)));

            if (existing != null) return existing;

            var created = new DeviceVolumeProfile(deviceId, deviceName);
            Settings.VolumeProfiles.Add(created);
            Save();
            return created;
        }

        public void SaveVolumeProfile(DeviceVolumeProfile profile)
        {
            var existingIndex = Settings.VolumeProfiles.FindIndex(p =>
                (!string.IsNullOrEmpty(profile.DeviceId) && string.Equals(p.DeviceId, profile.DeviceId, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(profile.DeviceName) && string.Equals(p.DeviceName, profile.DeviceName, StringComparison.OrdinalIgnoreCase)));

            if (existingIndex >= 0)
            {
                Settings.VolumeProfiles[existingIndex] = profile;
            }
            else
            {
                Settings.VolumeProfiles.Add(profile);
            }
            Save();
        }

        public int GetSavedDeviceVolume(string? deviceId, string? deviceName, int defaultPercent = 50)
        {
            if (Settings.SavedDeviceVolumes == null)
                return Math.Clamp(defaultPercent, 0, 100);

            if (!string.IsNullOrEmpty(deviceId) && Settings.SavedDeviceVolumes.TryGetValue(deviceId, out int volById))
            {
                return Math.Clamp(volById, 0, 100);
            }
            if (!string.IsNullOrEmpty(deviceName) && Settings.SavedDeviceVolumes.TryGetValue(deviceName, out int volByName))
            {
                return Math.Clamp(volByName, 0, 100);
            }
            return Math.Clamp(defaultPercent, 0, 100);
        }

        public void SetSavedDeviceVolume(string? deviceId, string? deviceName, int percent)
        {
            if (Settings.SavedDeviceVolumes == null)
                Settings.SavedDeviceVolumes = new(StringComparer.OrdinalIgnoreCase);

            int clamped = Math.Clamp(percent, 0, 100);
            bool changed = false;

            if (!string.IsNullOrEmpty(deviceId))
            {
                if (!Settings.SavedDeviceVolumes.TryGetValue(deviceId, out int cur) || cur != clamped)
                {
                    Settings.SavedDeviceVolumes[deviceId] = clamped;
                    changed = true;
                }
            }

            if (!string.IsNullOrEmpty(deviceName))
            {
                if (!Settings.SavedDeviceVolumes.TryGetValue(deviceName, out int cur) || cur != clamped)
                {
                    Settings.SavedDeviceVolumes[deviceName] = clamped;
                    changed = true;
                }
            }

            if (changed)
            {
                Save();
            }
        }

        public bool GetSavedDeviceMute(string? deviceId, string? deviceName, bool defaultMute = false)
        {
            if (Settings.SavedDeviceMutes == null)
                return defaultMute;

            if (!string.IsNullOrEmpty(deviceId) && Settings.SavedDeviceMutes.TryGetValue(deviceId, out bool muteById))
            {
                return muteById;
            }
            if (!string.IsNullOrEmpty(deviceName) && Settings.SavedDeviceMutes.TryGetValue(deviceName, out bool muteByName))
            {
                return muteByName;
            }
            return defaultMute;
        }

        public void SetSavedDeviceMute(string? deviceId, string? deviceName, bool isMuted)
        {
            if (Settings.SavedDeviceMutes == null)
                Settings.SavedDeviceMutes = new(StringComparer.OrdinalIgnoreCase);

            bool changed = false;

            if (!string.IsNullOrEmpty(deviceId))
            {
                if (!Settings.SavedDeviceMutes.TryGetValue(deviceId, out bool cur) || cur != isMuted)
                {
                    Settings.SavedDeviceMutes[deviceId] = isMuted;
                    changed = true;
                }
            }

            if (!string.IsNullOrEmpty(deviceName))
            {
                if (!Settings.SavedDeviceMutes.TryGetValue(deviceName, out bool cur) || cur != isMuted)
                {
                    Settings.SavedDeviceMutes[deviceName] = isMuted;
                    changed = true;
                }
            }

            if (changed)
            {
                Save();
            }
        }

        public void Save()
        {
            try
            {
                if (!Directory.Exists(_settingsFolder))
                {
                    Directory.CreateDirectory(_settingsFolder);
                }

                string json = JsonSerializer.Serialize(Settings, _jsonOptions);
                File.WriteAllText(_settingsFilePath, json);
                SettingsChanged?.Invoke();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save settings: {ex.Message}");
            }
        }

        private AppSettings LoadSettings()
        {
            try
            {
                if (File.Exists(_settingsFilePath))
                {
                    string json = File.ReadAllText(_settingsFilePath);
                    var settings = JsonSerializer.Deserialize<AppSettings>(json);
                    if (settings != null)
                        return settings;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load settings: {ex.Message}");
            }

            return new AppSettings();
        }
    }
}
