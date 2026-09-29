using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace AudioSwitcher.CoreAudio
{
    public enum TestSignalType
    {
        PinkNoise,
        HarmonicChord,
        PulsedChime
    }

    public class WasapiTestTonePlayer : IDisposable
    {
        private static WasapiTestTonePlayer? _instance;
        public static WasapiTestTonePlayer Instance => _instance ??= new WasapiTestTonePlayer();

        private readonly IMMDeviceEnumerator _enumerator;
        private static readonly Guid IID_IAudioClient = new("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2");
        private static readonly Guid IID_IAudioRenderClient = new("F294ACFC-3146-4483-A7BF-ADDCA7C260E2");

        private CancellationTokenSource? _playbackCts;
        private readonly object _lock = new();

        public bool IsPlaying { get; private set; }
        public string? CurrentlyPlayingDeviceId { get; private set; }

        public event Action<string?>? ActivePlaybackChanged;

        public WasapiTestTonePlayer()
        {
            _enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
        }

        public void StartSingle(string deviceId, TestSignalType signalType)
        {
            Stop();

            lock (_lock)
            {
                _playbackCts = new CancellationTokenSource();
                var token = _playbackCts.Token;
                CurrentlyPlayingDeviceId = deviceId;
                IsPlaying = true;
                ActivePlaybackChanged?.Invoke(deviceId);

                Task.Run(() => PlayLoop(deviceId, signalType, token), token);
            }
        }

        public void StartAlternate(string device1Id, string device2Id, TestSignalType signalType, int intervalMs = 2000)
        {
            Stop();

            lock (_lock)
            {
                _playbackCts = new CancellationTokenSource();
                var token = _playbackCts.Token;
                IsPlaying = true;

                Task.Run(async () =>
                {
                    bool toggle = false;
                    while (!token.IsCancellationRequested)
                    {
                        string currentId = toggle ? device2Id : device1Id;
                        CurrentlyPlayingDeviceId = currentId;
                        ActivePlaybackChanged?.Invoke(currentId);

                        using var subCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                        subCts.CancelAfter(intervalMs);

                        try
                        {
                            PlayLoop(currentId, signalType, subCts.Token);
                        }
                        catch { }

                        toggle = !toggle;
                        if (!token.IsCancellationRequested)
                        {
                            await Task.Delay(120, token); // Small gap between switches
                        }
                    }
                }, token);
            }
        }

        public void StartSimultaneous(string device1Id, string device2Id, TestSignalType signalType)
        {
            Stop();

            lock (_lock)
            {
                _playbackCts = new CancellationTokenSource();
                var token = _playbackCts.Token;
                CurrentlyPlayingDeviceId = $"{device1Id}+{device2Id}";
                IsPlaying = true;
                ActivePlaybackChanged?.Invoke(CurrentlyPlayingDeviceId);

                Task.Run(() => PlayLoop(device1Id, signalType, token), token);
                Task.Run(() => PlayLoop(device2Id, signalType, token), token);
            }
        }

        public void Stop()
        {
            lock (_lock)
            {
                if (_playbackCts != null)
                {
                    try
                    {
                        _playbackCts.Cancel();
                        _playbackCts.Dispose();
                    }
                    catch { }
                    _playbackCts = null;
                }
                IsPlaying = false;
                CurrentlyPlayingDeviceId = null;
                ActivePlaybackChanged?.Invoke(null);
            }
        }

        private void PlayLoop(string deviceId, TestSignalType signalType, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(deviceId)) return;

            IMMDevice? device = null;
            IAudioClient? audioClient = null;
            IAudioRenderClient? renderClient = null;
            IntPtr pFormat = IntPtr.Zero;

            try
            {
                int hr = _enumerator.GetDevice(deviceId, out device);
                if (hr != 0 || device == null) return;

                var iid = IID_IAudioClient;
                hr = device.Activate(ref iid, CLSCTX.CLSCTX_ALL, IntPtr.Zero, out object ppClient);
                if (hr != 0 || ppClient is not IAudioClient client) return;
                audioClient = client;

                hr = audioClient.GetMixFormat(out pFormat);
                if (hr != 0 || pFormat == IntPtr.Zero) return;

                var waveFormat = Marshal.PtrToStructure<WAVEFORMATEX>(pFormat);
                int channels = waveFormat.nChannels > 0 ? waveFormat.nChannels : 2;
                int sampleRate = waveFormat.nSamplesPerSec > 0 ? (int)waveFormat.nSamplesPerSec : 48000;
                int bitsPerSample = waveFormat.wBitsPerSample > 0 ? waveFormat.wBitsPerSample : 32;

                // 200ms buffer duration (in 100ns units)
                long hnsBufferDuration = 2000000;
                var sessionGuid = Guid.Empty;

                hr = audioClient.Initialize(AUDCLNT_SHAREMODE.AUDCLNT_SHAREMODE_SHARED, 0, hnsBufferDuration, 0, pFormat, ref sessionGuid);
                if (hr != 0) return;

                hr = audioClient.GetBufferSize(out uint bufferFrameCount);
                if (hr != 0 || bufferFrameCount == 0) return;

                var iidRender = IID_IAudioRenderClient;
                hr = audioClient.GetService(ref iidRender, out IntPtr pRender);
                if (hr != 0 || pRender == IntPtr.Zero) return;
                renderClient = (IAudioRenderClient)Marshal.GetObjectForIUnknown(pRender);
                Marshal.Release(pRender);

                audioClient.Start();

                // Pink noise filter state
                double b0 = 0, b1 = 0, b2 = 0, b3 = 0, b4 = 0, b5 = 0, b6 = 0;
                var rand = new Random();
                double phase = 0.0;
                double pulsePhase = 0.0;

                while (!token.IsCancellationRequested)
                {
                    hr = audioClient.GetCurrentPadding(out uint paddingFrames);
                    if (hr != 0) break;

                    uint numFramesAvailable = bufferFrameCount - paddingFrames;
                    if (numFramesAvailable > 0)
                    {
                        hr = renderClient.GetBuffer(numFramesAvailable, out IntPtr pData);
                        if (hr == 0 && pData != IntPtr.Zero)
                        {
                            int totalSamples = (int)numFramesAvailable * channels;

                            if (bitsPerSample == 32)
                            {
                                float[] samples = new float[totalSamples];
                                for (int i = 0; i < (int)numFramesAvailable; i++)
                                {
                                    float s = GenerateSample(signalType, sampleRate, ref phase, ref pulsePhase, rand, ref b0, ref b1, ref b2, ref b3, ref b4, ref b5, ref b6);
                                    for (int ch = 0; ch < channels; ch++)
                                    {
                                        samples[i * channels + ch] = s;
                                    }
                                }
                                Marshal.Copy(samples, 0, pData, totalSamples);
                            }
                            else if (bitsPerSample == 24)
                            {
                                byte[] samples = new byte[totalSamples * 3];
                                for (int i = 0; i < (int)numFramesAvailable; i++)
                                {
                                    float s = GenerateSample(signalType, sampleRate, ref phase, ref pulsePhase, rand, ref b0, ref b1, ref b2, ref b3, ref b4, ref b5, ref b6);
                                    int val = (int)Math.Clamp((int)(s * 8388607.0f), -8388608, 8388607);
                                    byte b0_v = (byte)(val & 0xFF);
                                    byte b1_v = (byte)((val >> 8) & 0xFF);
                                    byte b2_v = (byte)((val >> 16) & 0xFF);
                                    for (int ch = 0; ch < channels; ch++)
                                    {
                                        int off = (i * channels + ch) * 3;
                                        samples[off] = b0_v;
                                        samples[off + 1] = b1_v;
                                        samples[off + 2] = b2_v;
                                    }
                                }
                                Marshal.Copy(samples, 0, pData, samples.Length);
                            }
                            else if (bitsPerSample == 16)
                            {
                                short[] samples = new short[totalSamples];
                                for (int i = 0; i < (int)numFramesAvailable; i++)
                                {
                                    float s = GenerateSample(signalType, sampleRate, ref phase, ref pulsePhase, rand, ref b0, ref b1, ref b2, ref b3, ref b4, ref b5, ref b6);
                                    short val = (short)Math.Clamp((int)(s * 32767.0f), -32768, 32767);
                                    for (int ch = 0; ch < channels; ch++)
                                    {
                                        samples[i * channels + ch] = val;
                                    }
                                }
                                Marshal.Copy(samples, 0, pData, totalSamples);
                            }

                            renderClient.ReleaseBuffer(numFramesAvailable, 0);
                        }
                    }

                    Thread.Sleep(20);
                }

                audioClient.Stop();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"WASAPI PlayLoop error: {ex.Message}");
            }
            finally
            {
                if (pFormat != IntPtr.Zero) Marshal.FreeCoTaskMem(pFormat);
                if (renderClient != null) Marshal.ReleaseComObject(renderClient);
                if (audioClient != null) Marshal.ReleaseComObject(audioClient);
                if (device != null) Marshal.ReleaseComObject(device);
            }
        }

        private static float GenerateSample(
            TestSignalType type,
            int sampleRate,
            ref double phase,
            ref double pulsePhase,
            Random rand,
            ref double b0, ref double b1, ref double b2, ref double b3, ref double b4, ref double b5, ref double b6)
        {
            switch (type)
            {
                case TestSignalType.PinkNoise:
                    // Paul Kellet's filtered pink noise generator (-3dB/octave)
                    double white = rand.NextDouble() * 2.0 - 1.0;
                    b0 = 0.99886 * b0 + white * 0.0555179;
                    b1 = 0.99332 * b1 + white * 0.0750759;
                    b2 = 0.96900 * b2 + white * 0.1538520;
                    b3 = 0.86650 * b3 + white * 0.3104856;
                    b4 = 0.55000 * b4 + white * 0.5329522;
                    b5 = -0.7616 * b5 - white * 0.0168980;
                    double pink = b0 + b1 + b2 + b3 + b4 + b5 + b6 + white * 0.5362;
                    b6 = white * 0.115926;
                    return (float)(pink * 0.18); // Clear, pleasant reference loudness

                case TestSignalType.HarmonicChord:
                    // Warm pleasant chord (C major: 261.63Hz + 329.63Hz + 392.00Hz + 523.25Hz)
                    phase += 1.0 / sampleRate;
                    if (phase > 10.0) phase -= 10.0;
                    double s1 = Math.Sin(2.0 * Math.PI * 261.63 * phase);
                    double s2 = Math.Sin(2.0 * Math.PI * 329.63 * phase);
                    double s3 = Math.Sin(2.0 * Math.PI * 392.00 * phase);
                    double s4 = Math.Sin(2.0 * Math.PI * 523.25 * phase);
                    return (float)((s1 * 0.35 + s2 * 0.3 + s3 * 0.25 + s4 * 0.2) * 0.50);

                case TestSignalType.PulsedChime:
                    // Rhythmic pulse chime
                    phase += 1.0 / sampleRate;
                    pulsePhase += 1.0 / sampleRate;
                    if (pulsePhase >= 0.5) pulsePhase -= 0.5; // 2 pulses per second
                    double env = Math.Exp(-pulsePhase * 9.0); // Fast attack, gentle decay
                    double tone = Math.Sin(2.0 * Math.PI * 440.0 * phase) * 0.6 + Math.Sin(2.0 * Math.PI * 880.0 * phase) * 0.4;
                    return (float)(tone * env * 0.60);

                default:
                    return 0.0f;
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
