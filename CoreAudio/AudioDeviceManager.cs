using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace AudioSwitcher.CoreAudio
{
    public class AudioDeviceManager : IMMNotificationClient, IDisposable
    {
        private static AudioDeviceManager? _instance;
        public static AudioDeviceManager Instance => _instance ??= new AudioDeviceManager();

        private readonly IMMDeviceEnumerator _enumerator;
        private readonly PolicyConfigClient _policyConfigClient;
        private readonly System.Threading.Timer _pollTimer;
        private readonly object _lock = new();

        public event Action? DevicesUpdated;
        public event Action<AudioCategory, string>? DefaultDeviceChanged;

        private List<AudioDevice> _cachedDevices = new();

        public AudioDeviceManager()
        {
            _enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            _policyConfigClient = new PolicyConfigClient();

            // Register CoreAudio COM notifications
            try
            {
                _enumerator.RegisterEndpointNotificationCallback(this);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to register CoreAudio notifications: {ex.Message}");
            }

            // Refresh initial device list
            RefreshDevices();

            // Setup a fallback periodic refresh timer (every 4 seconds) to guarantee detection of Bluetooth reconnects
            _pollTimer = new System.Threading.Timer(_ =>
            {
                RefreshDevices();
            }, null, TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(4));
        }

        public IReadOnlyList<AudioDevice> GetDevices(EDataFlow dataFlow = EDataFlow.eAll)
        {
            lock (_lock)
            {
                if (dataFlow == EDataFlow.eAll)
                    return _cachedDevices.ToList();

                return _cachedDevices.Where(d => d.DataFlow == dataFlow).ToList();
            }
        }

        public AudioDevice? GetDeviceById(string id)
        {
            lock (_lock)
            {
                return _cachedDevices.FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));
            }
        }

        public AudioDevice? GetDefaultDevice(AudioCategory category)
        {
            lock (_lock)
            {
                return _cachedDevices.FirstOrDefault(d => d.IsDefault(category));
            }
        }

        public void RefreshDevices()
        {
            try
            {
                var newDevices = new List<AudioDevice>();

                // Get current default device IDs
                string? defaultRenderConsole = GetDefaultDeviceId(EDataFlow.eRender, ERole.eConsole);
                string? defaultRenderComms = GetDefaultDeviceId(EDataFlow.eRender, ERole.eCommunications);
                string? defaultCaptureConsole = GetDefaultDeviceId(EDataFlow.eCapture, ERole.eConsole);
                string? defaultCaptureComms = GetDefaultDeviceId(EDataFlow.eCapture, ERole.eCommunications);

                // Enumerate Render and Capture endpoints
                newDevices.AddRange(EnumerateEndpoints(EDataFlow.eRender, defaultRenderConsole, defaultRenderComms));
                newDevices.AddRange(EnumerateEndpoints(EDataFlow.eCapture, defaultCaptureConsole, defaultCaptureComms));

                bool changed = false;
                lock (_lock)
                {
                    if (_cachedDevices.Count != newDevices.Count)
                    {
                        changed = true;
                    }
                    else
                    {
                        for (int i = 0; i < newDevices.Count; i++)
                        {
                            var a = newDevices[i];
                            var b = _cachedDevices.FirstOrDefault(d => d.Id == a.Id);
                            if (b == null || b.State != a.State || b.IsDefaultSound != a.IsDefaultSound || b.IsDefaultCommunications != a.IsDefaultCommunications)
                            {
                                changed = true;
                                break;
                            }
                        }
                    }

                    _cachedDevices = newDevices;
                }

                if (changed)
                {
                    DevicesUpdated?.Invoke();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"RefreshDevices error: {ex.Message}");
            }
        }

        private string? GetDefaultDeviceId(EDataFlow flow, ERole role)
        {
            try
            {
                int hr = _enumerator.GetDefaultAudioEndpoint(flow, role, out IMMDevice endpoint);
                if (hr == 0 && endpoint != null)
                {
                    endpoint.GetId(out string id);
                    Marshal.ReleaseComObject(endpoint);
                    return id;
                }
            }
            catch { }
            return null;
        }

        private List<AudioDevice> EnumerateEndpoints(EDataFlow dataFlow, string? defaultConsoleId, string? defaultCommsId)
        {
            var list = new List<AudioDevice>();
            int hr = _enumerator.EnumAudioEndpoints(dataFlow, EDeviceState.DEVICE_STATEMASK_ALL, out IMMDeviceCollection collection);
            if (hr != 0 || collection == null)
                return list;

            try
            {
                collection.GetCount(out uint count);
                for (uint i = 0; i < count; i++)
                {
                    if (collection.Item(i, out IMMDevice device) == 0 && device != null)
                    {
                        try
                        {
                            device.GetId(out string id);
                            device.GetState(out EDeviceState state);

                            string friendlyName = string.Empty;
                            string desc = string.Empty;

                            if (device.OpenPropertyStore(EStgmAccess.STGM_READ, out IPropertyStore store) == 0 && store != null)
                            {
                                try
                                {
                                    var keyName = PROPERTYKEY.PKEY_Device_FriendlyName;
                                    if (store.GetValue(ref keyName, out PropVariant pvName) == 0)
                                    {
                                        friendlyName = pvName.GetValue() ?? string.Empty;
                                        pvName.Clear();
                                    }

                                    var keyDesc = PROPERTYKEY.PKEY_Device_DeviceDesc;
                                    if (store.GetValue(ref keyDesc, out PropVariant pvDesc) == 0)
                                    {
                                        desc = pvDesc.GetValue() ?? string.Empty;
                                        pvDesc.Clear();
                                    }
                                }
                                finally
                                {
                                    Marshal.ReleaseComObject(store);
                                }
                            }

                            if (string.IsNullOrWhiteSpace(friendlyName))
                            {
                                friendlyName = string.IsNullOrWhiteSpace(desc) ? (dataFlow == EDataFlow.eRender ? "Audio Output" : "Audio Input") : desc;
                            }

                            list.Add(new AudioDevice
                            {
                                Id = id,
                                Name = friendlyName,
                                DeviceDesc = desc,
                                DataFlow = dataFlow,
                                State = state,
                                IsDefaultSound = string.Equals(id, defaultConsoleId, StringComparison.OrdinalIgnoreCase),
                                IsDefaultCommunications = string.Equals(id, defaultCommsId, StringComparison.OrdinalIgnoreCase)
                            });
                        }
                        finally
                        {
                            Marshal.ReleaseComObject(device);
                        }
                    }
                }
            }
            finally
            {
                Marshal.ReleaseComObject(collection);
            }

            return list;
        }

        public bool SetDefaultDevice(string deviceId, AudioCategory category)
        {
            try
            {
                if (category == AudioCategory.OutputSound)
                {
                    _policyConfigClient.SetDefaultEndpoint(deviceId, ERole.eConsole);
                    _policyConfigClient.SetDefaultEndpoint(deviceId, ERole.eMultimedia);
                    if (Services.SettingsService.Instance.Settings.MatchOutputCommsToSound)
                    {
                        _policyConfigClient.SetDefaultEndpoint(deviceId, ERole.eCommunications);
                    }
                }
                else if (category == AudioCategory.OutputCommunications)
                {
                    _policyConfigClient.SetDefaultEndpoint(deviceId, ERole.eCommunications);
                }
                else if (category == AudioCategory.InputSound)
                {
                    _policyConfigClient.SetDefaultEndpoint(deviceId, ERole.eConsole);
                    _policyConfigClient.SetDefaultEndpoint(deviceId, ERole.eMultimedia);
                    if (Services.SettingsService.Instance.Settings.MatchInputCommsToSound)
                    {
                        _policyConfigClient.SetDefaultEndpoint(deviceId, ERole.eCommunications);
                    }
                }
                else if (category == AudioCategory.InputCommunications)
                {
                    _policyConfigClient.SetDefaultEndpoint(deviceId, ERole.eCommunications);
                }

                RefreshDevices();
                DefaultDeviceChanged?.Invoke(category, deviceId);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error setting default device: {ex.Message}");
                return false;
            }
        }

        #region IMMNotificationClient Implementation

        public void OnDeviceStateChanged(string deviceId, EDeviceState newState)
        {
            RefreshDevices();
        }

        public void OnDeviceAdded(string pwstrDeviceId)
        {
            RefreshDevices();
        }

        public void OnDeviceRemoved(string deviceId)
        {
            RefreshDevices();
        }

        public void OnDefaultDeviceChanged(EDataFlow flow, ERole role, string defaultDeviceId)
        {
            RefreshDevices();

            AudioCategory? category = (flow, role) switch
            {
                (EDataFlow.eRender, ERole.eConsole or ERole.eMultimedia) => AudioCategory.OutputSound,
                (EDataFlow.eRender, ERole.eCommunications) => AudioCategory.OutputCommunications,
                (EDataFlow.eCapture, ERole.eConsole or ERole.eMultimedia) => AudioCategory.InputSound,
                (EDataFlow.eCapture, ERole.eCommunications) => AudioCategory.InputCommunications,
                _ => null
            };

            if (category.HasValue && !string.IsNullOrEmpty(defaultDeviceId))
            {
                DefaultDeviceChanged?.Invoke(category.Value, defaultDeviceId);
            }
        }

        public void OnPropertyValueChanged(string pwstrDeviceId, PROPERTYKEY key)
        {
            // Minor property changes, refresh if needed
            RefreshDevices();
        }

        #endregion

        public void Dispose()
        {
            _pollTimer.Dispose();
            try
            {
                _enumerator.UnregisterEndpointNotificationCallback(this);
            }
            catch { }
        }
    }
}
