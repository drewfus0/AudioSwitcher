using System;
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

        public event Action<AudioCategory, AudioDevice>? DeviceAutoSwitched;

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

            var priorities = _settingsService.GetPriorities(category);
            if (priorities.Count == 0)
                return;

            var flow = category.GetDataFlow();
            var activeDevices = _deviceManager.GetDevices(flow)
                .Where(d => d.IsActive)
                .ToList();

            if (activeDevices.Count == 0)
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
                bool success = _deviceManager.SetDefaultDevice(targetDevice.Id, category);
                if (success)
                {
                    DeviceAutoSwitched?.Invoke(category, targetDevice);
                }
            }
        }
    }
}
