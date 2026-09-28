using System;
using System.Collections.Generic;
using System.IO;
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

    public class AppSettings
    {
        public List<PriorityDeviceEntry> OutputSoundPriorities { get; set; } = new();
        public List<PriorityDeviceEntry> OutputCommsPriorities { get; set; } = new();
        public List<PriorityDeviceEntry> InputSoundPriorities { get; set; } = new();
        public List<PriorityDeviceEntry> InputCommsPriorities { get; set; } = new();

        public bool AutoSwitchEnabled { get; set; } = true;
        public bool AutoSwitchOutputSound { get; set; } = true;
        public bool AutoSwitchOutputComms { get; set; } = true;
        public bool AutoSwitchInputSound { get; set; } = true;
        public bool AutoSwitchInputComms { get; set; } = true;

        public bool ShowSwitchNotifications { get; set; } = true;
        public bool StartWithWindows { get; set; } = false;
        public bool StartMinimized { get; set; } = true;
        public bool CloseToTray { get; set; } = true;
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

        public List<PriorityDeviceEntry> GetPriorities(AudioCategory category) => category switch
        {
            AudioCategory.OutputSound => Settings.OutputSoundPriorities,
            AudioCategory.OutputCommunications => Settings.OutputCommsPriorities,
            AudioCategory.InputSound => Settings.InputSoundPriorities,
            AudioCategory.InputCommunications => Settings.InputCommsPriorities,
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
