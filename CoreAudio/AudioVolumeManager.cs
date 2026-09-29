using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace AudioSwitcher.CoreAudio
{
    public class AudioVolumeManager : IDisposable
    {
        private static AudioVolumeManager? _instance;
        public static AudioVolumeManager Instance => _instance ??= new AudioVolumeManager();

        private readonly IMMDeviceEnumerator _enumerator;
        private static readonly Guid IID_IAudioEndpointVolume = new("5CDF2C82-841E-4546-9722-0CF74078229A");

        private readonly ConcurrentDictionary<string, float> _cachedVolumes = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, bool> _cachedMutes = new(StringComparer.OrdinalIgnoreCase);
        private readonly System.Threading.Timer _monitorTimer;
        private int _isPolling = 0;

        public event Action<string, float, bool>? VolumeChanged;

        public AudioVolumeManager()
        {
            _enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();

            // Pre-seed cache from persistent settings so unplugged devices retain volume across app restarts
            try
            {
                var savedVols = Services.SettingsService.Instance.Settings.SavedDeviceVolumes;
                if (savedVols != null)
                {
                    foreach (var kvp in savedVols)
                    {
                        _cachedVolumes[kvp.Key] = kvp.Value / 100.0f;
                    }
                }

                var savedMutes = Services.SettingsService.Instance.Settings.SavedDeviceMutes;
                if (savedMutes != null)
                {
                    foreach (var kvp in savedMutes)
                    {
                        _cachedMutes[kvp.Key] = kvp.Value;
                    }
                }
            }
            catch { }

            // Initial poll to seed live cache
            PollActiveVolumes();

            // Monitor active endpoints every 200ms for volume adjustments made outside our app
            _monitorTimer = new System.Threading.Timer(_ =>
            {
                PollActiveVolumes();
            }, null, TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(200));
        }

        private void PollActiveVolumes()
        {
            if (Interlocked.CompareExchange(ref _isPolling, 1, 0) != 0)
                return;

            try
            {
                var activeRender = AudioDeviceManager.Instance.GetDevices(EDataFlow.eRender)
                    .Where(d => d.IsActive)
                    .ToList();

                foreach (var dev in activeRender)
                {
                    if (TryQueryEndpoint(dev.Id, out float currentVol, out bool currentMute))
                    {
                        bool hadVol = _cachedVolumes.TryGetValue(dev.Id, out float prevVol);
                        bool hadMute = _cachedMutes.TryGetValue(dev.Id, out bool prevMute);

                        _cachedVolumes[dev.Id] = currentVol;
                        if (!string.IsNullOrEmpty(dev.Name)) _cachedVolumes[dev.Name] = currentVol;

                        _cachedMutes[dev.Id] = currentMute;
                        if (!string.IsNullOrEmpty(dev.Name)) _cachedMutes[dev.Name] = currentMute;

                        int currentPercent = (int)Math.Round(currentVol * 100.0f);
                        Services.SettingsService.Instance.SetSavedDeviceVolume(dev.Id, dev.Name, currentPercent);
                        Services.SettingsService.Instance.SetSavedDeviceMute(dev.Id, dev.Name, currentMute);

                        if (!hadVol || Math.Abs(currentVol - prevVol) > 0.005f || (hadMute && currentMute != prevMute))
                        {
                            VolumeChanged?.Invoke(dev.Id, currentVol, currentMute);
                        }
                    }
                }
            }
            catch { }
            finally
            {
                Interlocked.Exchange(ref _isPolling, 0);
            }
        }

        private bool TryQueryEndpoint(string deviceId, out float volume, out bool isMuted)
        {
            volume = 0.5f;
            isMuted = false;
            if (string.IsNullOrWhiteSpace(deviceId)) return false;

            IMMDevice? device = null;
            try
            {
                int hr = _enumerator.GetDevice(deviceId, out device);
                if (hr == 0 && device != null)
                {
                    var iid = IID_IAudioEndpointVolume;
                    hr = device.Activate(ref iid, CLSCTX.CLSCTX_ALL, IntPtr.Zero, out IntPtr pInterface);
                    if (hr == 0 && pInterface != IntPtr.Zero)
                    {
                        var endpointVolume = (IAudioEndpointVolume)Marshal.GetObjectForIUnknown(pInterface);
                        try
                        {
                            bool gotVol = endpointVolume.GetMasterVolumeLevelScalar(out float level) == 0;
                            bool gotMute = endpointVolume.GetMute(out bool mute) == 0;
                            if (gotVol)
                            {
                                volume = Math.Clamp(level, 0.0f, 1.0f);
                                isMuted = gotMute && mute;
                                return true;
                            }
                        }
                        finally
                        {
                            Marshal.Release(pInterface);
                        }
                    }
                }
            }
            catch { }
            finally
            {
                if (device != null) Marshal.ReleaseComObject(device);
            }

            return false;
        }

        public float GetVolume(string deviceId)
        {
            if (string.IsNullOrWhiteSpace(deviceId)) return 0.5f;

            if (TryQueryEndpoint(deviceId, out float liveVol, out bool liveMute))
            {
                _cachedVolumes[deviceId] = liveVol;
                _cachedMutes[deviceId] = liveMute;
                return liveVol;
            }

            // Fallback to cached last known volume if device was just unplugged
            if (_cachedVolumes.TryGetValue(deviceId, out float cached))
            {
                return cached;
            }

            // Fallback to persisted saved volume in settings
            int savedVol = Services.SettingsService.Instance.GetSavedDeviceVolume(deviceId, null, -1);
            if (savedVol >= 0)
            {
                float volScalar = savedVol / 100.0f;
                _cachedVolumes[deviceId] = volScalar;
                return volScalar;
            }

            return 0.5f;
        }

        public float GetLastKnownVolume(string deviceId)
        {
            if (string.IsNullOrWhiteSpace(deviceId)) return 0.5f;
            if (_cachedVolumes.TryGetValue(deviceId, out float cached))
            {
                return cached;
            }
            return GetVolume(deviceId);
        }

        public bool SetVolume(string deviceId, float volume)
        {
            if (string.IsNullOrWhiteSpace(deviceId)) return false;
            volume = Math.Clamp(volume, 0.0f, 1.0f);

            // Record in cache & settings immediately
            _cachedVolumes[deviceId] = volume;
            var dev = AudioDeviceManager.Instance.GetDeviceById(deviceId);
            if (dev != null && !string.IsNullOrEmpty(dev.Name))
            {
                _cachedVolumes[dev.Name] = volume;
            }

            int percent = (int)Math.Round(volume * 100.0f);
            Services.SettingsService.Instance.SetSavedDeviceVolume(deviceId, dev?.Name, percent);

            IMMDevice? device = null;
            try
            {
                int hr = _enumerator.GetDevice(deviceId, out device);
                if (hr != 0 || device == null)
                {
                    return false;
                }

                var iid = IID_IAudioEndpointVolume;
                hr = device.Activate(ref iid, CLSCTX.CLSCTX_ALL, IntPtr.Zero, out IntPtr pInterface);
                if (hr != 0 || pInterface == IntPtr.Zero)
                {
                    return false;
                }

                var endpointVolume = (IAudioEndpointVolume)Marshal.GetObjectForIUnknown(pInterface);
                try
                {
                    int setHr = endpointVolume.SetMasterVolumeLevelScalar(volume, IntPtr.Zero);
                    if (setHr == 0)
                    {
                        VolumeChanged?.Invoke(deviceId, volume, GetMute(deviceId));
                        return true;
                    }
                }
                finally
                {
                    Marshal.Release(pInterface);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SetVolume error: {ex.Message}");
            }
            finally
            {
                if (device != null) Marshal.ReleaseComObject(device);
            }

            return false;
        }

        public int GetVolumePercent(string deviceId)
        {
            float vol = GetVolume(deviceId);
            return (int)Math.Round(vol * 100.0f);
        }

        public bool SetVolumePercent(string deviceId, int percent)
        {
            percent = Math.Clamp(percent, 0, 100);
            return SetVolume(deviceId, percent / 100.0f);
        }

        public bool GetMute(string deviceId)
        {
            if (string.IsNullOrWhiteSpace(deviceId)) return false;

            if (TryQueryEndpoint(deviceId, out _, out bool liveMute))
            {
                _cachedMutes[deviceId] = liveMute;
                return liveMute;
            }

            if (_cachedMutes.TryGetValue(deviceId, out bool cachedMute))
            {
                return cachedMute;
            }

            return Services.SettingsService.Instance.GetSavedDeviceMute(deviceId, null, false);
        }

        public bool SetMute(string deviceId, bool mute)
        {
            if (string.IsNullOrWhiteSpace(deviceId)) return false;

            _cachedMutes[deviceId] = mute;
            var dev = AudioDeviceManager.Instance.GetDeviceById(deviceId);
            if (dev != null && !string.IsNullOrEmpty(dev.Name))
            {
                _cachedMutes[dev.Name] = mute;
            }

            Services.SettingsService.Instance.SetSavedDeviceMute(deviceId, dev?.Name, mute);

            IMMDevice? device = null;
            try
            {
                int hr = _enumerator.GetDevice(deviceId, out device);
                if (hr == 0 && device != null)
                {
                    var iid = IID_IAudioEndpointVolume;
                    hr = device.Activate(ref iid, CLSCTX.CLSCTX_ALL, IntPtr.Zero, out IntPtr pInterface);
                    if (hr == 0 && pInterface != IntPtr.Zero)
                    {
                        var endpointVolume = (IAudioEndpointVolume)Marshal.GetObjectForIUnknown(pInterface);
                        try
                        {
                            int setHr = endpointVolume.SetMute(mute, IntPtr.Zero);
                            if (setHr == 0)
                            {
                                VolumeChanged?.Invoke(deviceId, GetVolume(deviceId), mute);
                                return true;
                            }
                        }
                        finally
                        {
                            Marshal.Release(pInterface);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SetMute failed for {deviceId}: {ex.Message}");
            }
            finally
            {
                if (device != null) Marshal.ReleaseComObject(device);
            }

            return false;
        }

        public void Dispose()
        {
            _monitorTimer.Dispose();
        }
    }
}
