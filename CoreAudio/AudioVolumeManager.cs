using System;
using System.Runtime.InteropServices;

namespace AudioSwitcher.CoreAudio
{
    public class AudioVolumeManager
    {
        private static AudioVolumeManager? _instance;
        public static AudioVolumeManager Instance => _instance ??= new AudioVolumeManager();

        private readonly IMMDeviceEnumerator _enumerator;
        private static readonly Guid IID_IAudioEndpointVolume = new("5BC63904-734E-40E0-863A-B690174A3F43");

        public AudioVolumeManager()
        {
            _enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
        }

        public float GetVolume(string deviceId)
        {
            if (string.IsNullOrWhiteSpace(deviceId)) return 1.0f;

            try
            {
                int hr = _enumerator.GetDevice(deviceId, out IMMDevice device);
                if (hr == 0 && device != null)
                {
                    try
                    {
                        var iid = IID_IAudioEndpointVolume;
                        hr = device.Activate(ref iid, CLSCTX.CLSCTX_ALL, IntPtr.Zero, out object ppInterface);
                        if (hr == 0 && ppInterface is IAudioEndpointVolume endpointVolume)
                        {
                            try
                            {
                                if (endpointVolume.GetMasterVolumeLevelScalar(out float level) == 0)
                                {
                                    return Math.Clamp(level, 0.0f, 1.0f);
                                }
                            }
                            finally
                            {
                                Marshal.ReleaseComObject(endpointVolume);
                            }
                        }
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(device);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetVolume failed for {deviceId}: {ex.Message}");
            }

            return 1.0f;
        }

        public bool SetVolume(string deviceId, float volume)
        {
            if (string.IsNullOrWhiteSpace(deviceId)) return false;
            volume = Math.Clamp(volume, 0.0f, 1.0f);

            try
            {
                int hr = _enumerator.GetDevice(deviceId, out IMMDevice device);
                if (hr == 0 && device != null)
                {
                    try
                    {
                        var iid = IID_IAudioEndpointVolume;
                        hr = device.Activate(ref iid, CLSCTX.CLSCTX_ALL, IntPtr.Zero, out object ppInterface);
                        if (hr == 0 && ppInterface is IAudioEndpointVolume endpointVolume)
                        {
                            try
                            {
                                var guid = Guid.Empty;
                                int setHr = endpointVolume.SetMasterVolumeLevelScalar(volume, ref guid);
                                return setHr == 0;
                            }
                            finally
                            {
                                Marshal.ReleaseComObject(endpointVolume);
                            }
                        }
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(device);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SetVolume failed for {deviceId}: {ex.Message}");
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

            try
            {
                int hr = _enumerator.GetDevice(deviceId, out IMMDevice device);
                if (hr == 0 && device != null)
                {
                    try
                    {
                        var iid = IID_IAudioEndpointVolume;
                        hr = device.Activate(ref iid, CLSCTX.CLSCTX_ALL, IntPtr.Zero, out object ppInterface);
                        if (hr == 0 && ppInterface is IAudioEndpointVolume endpointVolume)
                        {
                            try
                            {
                                if (endpointVolume.GetMute(out bool isMuted) == 0)
                                {
                                    return isMuted;
                                }
                            }
                            finally
                            {
                                Marshal.ReleaseComObject(endpointVolume);
                            }
                        }
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(device);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetMute failed for {deviceId}: {ex.Message}");
            }

            return false;
        }

        public bool SetMute(string deviceId, bool mute)
        {
            if (string.IsNullOrWhiteSpace(deviceId)) return false;

            try
            {
                int hr = _enumerator.GetDevice(deviceId, out IMMDevice device);
                if (hr == 0 && device != null)
                {
                    try
                    {
                        var iid = IID_IAudioEndpointVolume;
                        hr = device.Activate(ref iid, CLSCTX.CLSCTX_ALL, IntPtr.Zero, out object ppInterface);
                        if (hr == 0 && ppInterface is IAudioEndpointVolume endpointVolume)
                        {
                            try
                            {
                                var guid = Guid.Empty;
                                int setHr = endpointVolume.SetMute(mute, ref guid);
                                return setHr == 0;
                            }
                            finally
                            {
                                Marshal.ReleaseComObject(endpointVolume);
                            }
                        }
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(device);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SetMute failed for {deviceId}: {ex.Message}");
            }

            return false;
        }
    }
}
