using System;
using System.Runtime.InteropServices;

namespace AudioSwitcher.CoreAudio
{
    public class AudioVolumeManager
    {
        private static AudioVolumeManager? _instance;
        public static AudioVolumeManager Instance => _instance ??= new AudioVolumeManager();

        private readonly IMMDeviceEnumerator _enumerator;
        private static readonly Guid IID_IAudioEndpointVolume = new("5CDF2C82-841E-4546-9722-0CF74078229A");

        public AudioVolumeManager()
        {
            _enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
        }

        public float GetVolume(string deviceId)
        {
            if (string.IsNullOrWhiteSpace(deviceId)) return 1.0f;

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
                            if (endpointVolume.GetMasterVolumeLevelScalar(out float level) == 0)
                            {
                                return Math.Clamp(level, 0.0f, 1.0f);
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
                System.Diagnostics.Debug.WriteLine($"GetVolume failed for {deviceId}: {ex.Message}");
            }
            finally
            {
                if (device != null) Marshal.ReleaseComObject(device);
            }

            return 1.0f;
        }

        public bool SetVolume(string deviceId, float volume)
        {
            if (string.IsNullOrWhiteSpace(deviceId)) return false;
            volume = Math.Clamp(volume, 0.0f, 1.0f);

            IMMDevice? device = null;
            try
            {
                int hr = _enumerator.GetDevice(deviceId, out device);
                if (hr != 0 || device == null)
                {
                    System.IO.File.AppendAllText("volume_test_output.txt", $"   [DEBUG] GetDevice HR=0x{hr:X8}\n");
                    return false;
                }

                var iid = IID_IAudioEndpointVolume;
                hr = device.Activate(ref iid, CLSCTX.CLSCTX_ALL, IntPtr.Zero, out IntPtr pInterface);
                if (hr != 0 || pInterface == IntPtr.Zero)
                {
                    System.IO.File.AppendAllText("volume_test_output.txt", $"   [DEBUG] Activate HR=0x{hr:X8}, pInterface=0x{pInterface.ToInt64():X}\n");
                    return false;
                }

                var endpointVolume = (IAudioEndpointVolume)Marshal.GetObjectForIUnknown(pInterface);
                try
                {
                    int setHr = endpointVolume.SetMasterVolumeLevelScalar(volume, IntPtr.Zero);
                    if (setHr != 0)
                    {
                        System.IO.File.AppendAllText("volume_test_output.txt", $"   [DEBUG] SetMasterVolumeLevelScalar HR=0x{setHr:X8}\n");
                    }
                    return setHr == 0;
                }
                finally
                {
                    Marshal.Release(pInterface);
                }
            }
            catch (Exception ex)
            {
                System.IO.File.AppendAllText("volume_test_output.txt", $"   [DEBUG] EXCEPTION: {ex}\n");
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
                            if (endpointVolume.GetMute(out bool isMuted) == 0)
                            {
                                return isMuted;
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
                System.Diagnostics.Debug.WriteLine($"GetMute failed for {deviceId}: {ex.Message}");
            }
            finally
            {
                if (device != null) Marshal.ReleaseComObject(device);
            }

            return false;
        }

        public bool SetMute(string deviceId, bool mute)
        {
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
                            int setHr = endpointVolume.SetMute(mute, IntPtr.Zero);
                            return setHr == 0;
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
    }
}
