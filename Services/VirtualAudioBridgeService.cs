using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using AudioSwitcher.CoreAudio;

namespace AudioSwitcher.Services
{
    public class VirtualAudioBridgeService : IDisposable
    {
        private static VirtualAudioBridgeService? _instance;
        public static VirtualAudioBridgeService Instance => _instance ??= new VirtualAudioBridgeService();

        private readonly AudioDeviceManager _deviceManager;
        private readonly SettingsService _settingsService;

        private readonly object _bridgeLock = new();
        private Thread? _bridgeThread;
        private CancellationTokenSource? _bridgeCts;
        private volatile bool _isRunning = false;

        // Endpoint information
        private string? _cableRenderDeviceId;   // "CABLE Input" (Render)
        private string? _cableCaptureDeviceId;  // "CABLE Output" (Capture)
        private string? _primaryTargetDeviceId; // Physical hardware output
        private readonly HashSet<string> _secondaryTargetDeviceIds = new(StringComparer.OrdinalIgnoreCase);

        // Track user preference & state
        public bool IsVirtualCableAvailable { get; private set; }
        public bool IsBridgeActive => _isRunning;
        public string? ActivePrimaryTargetDeviceId => _primaryTargetDeviceId;
        public IReadOnlyCollection<string> SecondaryTargetDeviceIds
        {
            get
            {
                lock (_bridgeLock)
                {
                    return _secondaryTargetDeviceIds.ToList();
                }
            }
        }

        public event Action<bool>? BridgeStateChanged;
        public event Action<string?>? PrimaryTargetChanged;

        private static readonly Guid IID_IAudioClient = new("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2");
        private static readonly Guid IID_IAudioCaptureClient = new("C8ADBD64-E71E-48A0-A4DE-185C395CD317");
        private static readonly Guid IID_IAudioRenderClient = new("F294ACFC-3146-4483-A7BF-ADDCA7C260E2");

        public VirtualAudioBridgeService()
        {
            _deviceManager = AudioDeviceManager.Instance;
            _settingsService = SettingsService.Instance;

            DetectVirtualCable();

            _deviceManager.DevicesUpdated += OnDevicesUpdated;
        }

        private void OnDevicesUpdated()
        {
            bool wasAvailable = IsVirtualCableAvailable;
            DetectVirtualCable();

            if (_isRunning && !IsVirtualCableAvailable)
            {
                // VB-Cable vanished or disabled, fallback to direct
                System.Diagnostics.Debug.WriteLine("[VirtualAudioBridge] VB-Cable endpoint lost. Stopping bridge.");
                StopBridge();
            }
        }

        public void DetectVirtualCable()
        {
            var renderDevices = _deviceManager.GetDevices(EDataFlow.eRender);
            var captureDevices = _deviceManager.GetDevices(EDataFlow.eCapture);

            var cableRender = renderDevices.FirstOrDefault(d =>
                d.IsActive &&
                d.Name.Contains("CABLE Input", StringComparison.OrdinalIgnoreCase) &&
                (d.Name.Contains("VB-Audio", StringComparison.OrdinalIgnoreCase) || d.DeviceDesc.Contains("VB-Audio", StringComparison.OrdinalIgnoreCase)));

            var cableCapture = captureDevices.FirstOrDefault(d =>
                d.IsActive &&
                d.Name.Contains("CABLE Output", StringComparison.OrdinalIgnoreCase) &&
                (d.Name.Contains("VB-Audio", StringComparison.OrdinalIgnoreCase) || d.DeviceDesc.Contains("VB-Audio", StringComparison.OrdinalIgnoreCase)));

            _cableRenderDeviceId = cableRender?.Id;
            _cableCaptureDeviceId = cableCapture?.Id;

            bool isAvailable = !string.IsNullOrEmpty(_cableRenderDeviceId) && !string.IsNullOrEmpty(_cableCaptureDeviceId);
            if (isAvailable != IsVirtualCableAvailable)
            {
                IsVirtualCableAvailable = isAvailable;
                BridgeStateChanged?.Invoke(_isRunning);
            }
        }

        public bool IsVirtualBridgeEnabled
        {
            get => _settingsService.Settings.UseVirtualAudioBridge;
            set
            {
                if (_settingsService.Settings.UseVirtualAudioBridge != value)
                {
                    _settingsService.Settings.UseVirtualAudioBridge = value;
                    _settingsService.Save();

                    if (value)
                    {
                        SwitchToVirtualBridge();
                    }
                    else
                    {
                        SwitchToDirectHardware();
                    }
                }
            }
        }

        public void SetPrimaryTargetDevice(string deviceId)
        {
            if (string.IsNullOrWhiteSpace(deviceId))
                return;

            // Prevent assigning VB-Cable as its own destination
            if (string.Equals(deviceId, _cableRenderDeviceId, StringComparison.OrdinalIgnoreCase))
                return;

            lock (_bridgeLock)
            {
                if (string.Equals(_primaryTargetDeviceId, deviceId, StringComparison.OrdinalIgnoreCase))
                    return;

                _primaryTargetDeviceId = deviceId;
            }

            PrimaryTargetChanged?.Invoke(deviceId);

            // If bridge is supposed to be running, ensure it's started
            if (IsVirtualBridgeEnabled && IsVirtualCableAvailable && !_isRunning)
            {
                StartBridge();
            }
        }

        public void SetSecondaryTargetDevices(IEnumerable<string> deviceIds)
        {
            lock (_bridgeLock)
            {
                _secondaryTargetDeviceIds.Clear();
                foreach (var id in deviceIds)
                {
                    if (!string.Equals(id, _cableRenderDeviceId, StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(id, _primaryTargetDeviceId, StringComparison.OrdinalIgnoreCase))
                    {
                        _secondaryTargetDeviceIds.Add(id);
                    }
                }
            }
        }

        public void SwitchToVirtualBridge()
        {
            DetectVirtualCable();
            if (!IsVirtualCableAvailable || string.IsNullOrEmpty(_cableRenderDeviceId))
            {
                System.Diagnostics.Debug.WriteLine("[VirtualAudioBridge] Cannot switch to virtual bridge: VB-Cable not detected.");
                return;
            }

            // 1. Set VB-CABLE Input as Windows default audio device
            _deviceManager.SetDefaultDevice(_cableRenderDeviceId, AudioCategory.OutputSound);
            if (_settingsService.IsCategoryMirrored(AudioCategory.OutputCommunications))
            {
                _deviceManager.SetDefaultDevice(_cableRenderDeviceId, AudioCategory.OutputCommunications);
            }

            // 2. Start bridge to primary target
            StartBridge();
        }

        public void SwitchToDirectHardware(string? specificTargetDeviceId = null)
        {
            // 1. Stop audio bridge
            StopBridge();

            // 2. Determine target hardware device to restore as Windows default
            string? targetId = specificTargetDeviceId ?? _primaryTargetDeviceId;

            if (string.IsNullOrEmpty(targetId))
            {
                // Fallback to highest priority available physical render device
                var activeDevices = _deviceManager.GetDevices(EDataFlow.eRender)
                    .Where(d => d.IsActive && !d.IsVirtualCable && !_settingsService.IsDeviceIgnored(d.Id, d.Name))
                    .ToList();

                var priorities = _settingsService.GetPriorities(AudioCategory.OutputSound);
                foreach (var entry in priorities)
                {
                    var match = activeDevices.FirstOrDefault(d => string.Equals(d.Id, entry.Id, StringComparison.OrdinalIgnoreCase));
                    if (match != null)
                    {
                        targetId = match.Id;
                        break;
                    }
                }

                targetId ??= activeDevices.FirstOrDefault()?.Id;
            }

            // 3. Restore Windows default to hardware
            if (!string.IsNullOrEmpty(targetId))
            {
                _deviceManager.SetDefaultDevice(targetId, AudioCategory.OutputSound);
                if (_settingsService.IsCategoryMirrored(AudioCategory.OutputCommunications))
                {
                    _deviceManager.SetDefaultDevice(targetId, AudioCategory.OutputCommunications);
                }
            }
        }

        public void RestoreDirectHardwareOnShutdown()
        {
            try
            {
                if (_isRunning || IsVirtualBridgeEnabled)
                {
                    SwitchToDirectHardware();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VirtualAudioBridge] Shutdown restore failed: {ex.Message}");
            }
        }

        public void StartBridge()
        {
            lock (_bridgeLock)
            {
                if (_isRunning) return;

                DetectVirtualCable();
                if (!IsVirtualCableAvailable || string.IsNullOrEmpty(_cableCaptureDeviceId))
                {
                    return;
                }

                _bridgeCts = new CancellationTokenSource();
                var token = _bridgeCts.Token;
                _isRunning = true;

                _bridgeThread = new Thread(() => BridgeLoop(token))
                {
                    Name = "AudioSwitcher_VirtualBridgeThread",
                    IsBackground = true,
                    Priority = ThreadPriority.AboveNormal
                };
                _bridgeThread.Start();
            }

            BridgeStateChanged?.Invoke(true);
        }

        public void StopBridge()
        {
            lock (_bridgeLock)
            {
                if (!_isRunning) return;

                _isRunning = false;
                try
                {
                    _bridgeCts?.Cancel();
                    _bridgeThread?.Join(1000);
                }
                catch { }

                _bridgeCts?.Dispose();
                _bridgeCts = null;
                _bridgeThread = null;
            }

            BridgeStateChanged?.Invoke(false);
        }

        private class ActiveRenderClient : IDisposable
        {
            public string DeviceId { get; }
            public IMMDevice Device { get; }
            public IAudioClient AudioClient { get; }
            public IAudioRenderClient RenderClient { get; }
            public uint BufferFrameCount { get; }

            public ActiveRenderClient(string deviceId, IMMDevice device, IAudioClient audioClient, IAudioRenderClient renderClient, uint bufferFrameCount)
            {
                DeviceId = deviceId;
                Device = device;
                AudioClient = audioClient;
                RenderClient = renderClient;
                BufferFrameCount = bufferFrameCount;
            }

            public void Dispose()
            {
                try { AudioClient.Stop(); } catch { }
                try { Marshal.ReleaseComObject(RenderClient); } catch { }
                try { Marshal.ReleaseComObject(AudioClient); } catch { }
                try { Marshal.ReleaseComObject(Device); } catch { }
            }
        }

        private void BridgeLoop(CancellationToken token)
        {
            IMMDeviceEnumerator? enumerator = null;
            IMMDevice? captureDev = null;
            IAudioClient? captureClient = null;
            IAudioCaptureClient? capture = null;
            IntPtr pFormat = IntPtr.Zero;

            ActiveRenderClient? primaryRender = null;
            var secondaryRenders = new List<ActiveRenderClient>();
            string? currentPrimaryId = null;

            try
            {
                enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();

                // 1. Initialize Capture on CABLE Output
                int hr = enumerator.GetDevice(_cableCaptureDeviceId!, out captureDev);
                if (hr != 0 || captureDev == null) return;

                var iidAudioClient = IID_IAudioClient;
                hr = captureDev.Activate(ref iidAudioClient, CLSCTX.CLSCTX_ALL, IntPtr.Zero, out IntPtr pCapClient);
                if (hr != 0 || pCapClient == IntPtr.Zero) return;
                captureClient = (IAudioClient)Marshal.GetObjectForIUnknown(pCapClient);
                Marshal.Release(pCapClient);

                hr = captureClient.GetMixFormat(out pFormat);
                if (hr != 0 || pFormat == IntPtr.Zero) return;

                var wf = Marshal.PtrToStructure<WAVEFORMATEX>(pFormat);
                int frameBytes = (wf.wBitsPerSample / 8) * wf.nChannels;
                if (frameBytes <= 0) frameBytes = 8; // fallback 32-bit stereo

                long hnsBufferDuration = 500000; // 50ms buffer
                var sessionGuid = Guid.Empty;
                hr = captureClient.Initialize(AUDCLNT_SHAREMODE.AUDCLNT_SHAREMODE_SHARED, 0, hnsBufferDuration, 0, pFormat, ref sessionGuid);
                if (hr != 0) return;

                var iidCapture = IID_IAudioCaptureClient;
                hr = captureClient.GetService(ref iidCapture, out IntPtr pCapService);
                if (hr != 0 || pCapService == IntPtr.Zero) return;
                capture = (IAudioCaptureClient)Marshal.GetObjectForIUnknown(pCapService);
                Marshal.Release(pCapService);

                captureClient.Start();

                // Main real-time audio forwarding loop
                while (!token.IsCancellationRequested && _isRunning)
                {
                    // Check if primary target changed
                    string? targetPrimaryId;
                    List<string> targetSecondaryIds;
                    lock (_bridgeLock)
                    {
                        targetPrimaryId = _primaryTargetDeviceId;
                        targetSecondaryIds = _secondaryTargetDeviceIds.ToList();
                    }

                    if (!string.Equals(currentPrimaryId, targetPrimaryId, StringComparison.OrdinalIgnoreCase))
                    {
                        // Safely transition primary target
                        var newRender = CreateRenderClientWithRetry(enumerator, targetPrimaryId, pFormat, hnsBufferDuration, token);
                        if (newRender != null)
                        {
                            primaryRender?.Dispose();
                            primaryRender = newRender;
                            currentPrimaryId = targetPrimaryId;
                        }
                    }

                    // Update secondary renders if multi-output is enabled
                    bool multiOutputEnabled = _settingsService.Settings.EnableMultiOutput;
                    if (!multiOutputEnabled && secondaryRenders.Count > 0)
                    {
                        foreach (var sr in secondaryRenders) sr.Dispose();
                        secondaryRenders.Clear();
                    }
                    else if (multiOutputEnabled)
                    {
                        SyncSecondaryRenders(enumerator, secondaryRenders, targetSecondaryIds, pFormat, hnsBufferDuration, token);
                    }

                    // Read captured audio packets
                    hr = capture.GetNextPacketSize(out uint packetSize);
                    if (hr != 0 || packetSize == 0)
                    {
                        Thread.Sleep(2);
                        continue;
                    }

                    while (packetSize > 0 && !token.IsCancellationRequested)
                    {
                        hr = capture.GetBuffer(out IntPtr pData, out uint numFrames, out uint flags, out _, out _);
                        if (hr == 0 && numFrames > 0)
                        {
                            bool isSilent = (flags & (uint)AUDCLNT_BUFFERFLAGS.SILENT) != 0;

                            // 1. Forward to Primary Render
                            if (primaryRender != null)
                            {
                                ForwardFrames(primaryRender, pData, numFrames, frameBytes, isSilent);
                            }

                            // 2. Forward to Secondary Renders (Multi-Output)
                            if (multiOutputEnabled && secondaryRenders.Count > 0)
                            {
                                for (int i = secondaryRenders.Count - 1; i >= 0; i--)
                                {
                                    var sec = secondaryRenders[i];
                                    bool ok = ForwardFrames(sec, pData, numFrames, frameBytes, isSilent);
                                    if (!ok)
                                    {
                                        sec.Dispose();
                                        secondaryRenders.RemoveAt(i);
                                    }
                                }
                            }

                            capture.ReleaseBuffer(numFrames);
                        }

                        hr = capture.GetNextPacketSize(out packetSize);
                        if (hr != 0) break;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VirtualAudioBridge] Loop exception: {ex}");
            }
            finally
            {
                primaryRender?.Dispose();
                foreach (var sr in secondaryRenders) sr.Dispose();

                try { captureClient?.Stop(); } catch { }
                if (capture != null) try { Marshal.ReleaseComObject(capture); } catch { }
                if (captureClient != null) try { Marshal.ReleaseComObject(captureClient); } catch { }
                if (captureDev != null) try { Marshal.ReleaseComObject(captureDev); } catch { }
                if (pFormat != IntPtr.Zero) Marshal.FreeCoTaskMem(pFormat);
                if (enumerator != null) try { Marshal.ReleaseComObject(enumerator); } catch { }
            }
        }

        private static bool ForwardFrames(ActiveRenderClient renderClient, IntPtr pSrcData, uint numFrames, int frameBytes, bool isSilent)
        {
            try
            {
                int hr = renderClient.AudioClient.GetCurrentPadding(out uint padding);
                if (hr != 0) return false;

                uint available = renderClient.BufferFrameCount - padding;
                if (available >= numFrames)
                {
                    hr = renderClient.RenderClient.GetBuffer(numFrames, out IntPtr pDstData);
                    if (hr == 0 && pDstData != IntPtr.Zero)
                    {
                        if (isSilent)
                        {
                            unsafe
                            {
                                byte* ptr = (byte*)pDstData;
                                int byteCount = (int)(numFrames * frameBytes);
                                for (int i = 0; i < byteCount; i++) ptr[i] = 0;
                            }
                        }
                        else
                        {
                            unsafe
                            {
                                Buffer.MemoryCopy((void*)pSrcData, (void*)pDstData, numFrames * frameBytes, numFrames * frameBytes);
                            }
                        }

                        renderClient.RenderClient.ReleaseBuffer(numFrames, 0);
                        return true;
                    }
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        private ActiveRenderClient? CreateRenderClientWithRetry(
            IMMDeviceEnumerator enumerator,
            string? deviceId,
            IntPtr pFormat,
            long hnsBufferDuration,
            CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(deviceId))
                return null;

            // Give newly connecting Bluetooth / USB endpoints up to 3 retry attempts (200ms apart) to finish handshake
            for (int attempt = 0; attempt < 3; attempt++)
            {
                if (token.IsCancellationRequested) return null;

                try
                {
                    int hr = enumerator.GetDevice(deviceId, out IMMDevice dev);
                    if (hr != 0 || dev == null)
                    {
                        Thread.Sleep(200);
                        continue;
                    }

                    var iidAudioClient = IID_IAudioClient;
                    hr = dev.Activate(ref iidAudioClient, CLSCTX.CLSCTX_ALL, IntPtr.Zero, out IntPtr pClient);
                    if (hr != 0 || pClient == IntPtr.Zero)
                    {
                        Marshal.ReleaseComObject(dev);
                        Thread.Sleep(200);
                        continue;
                    }

                    var audioClient = (IAudioClient)Marshal.GetObjectForIUnknown(pClient);
                    Marshal.Release(pClient);

                    // Auto-convert PCM rate / channel conversion in Windows
                    uint streamFlags = AudioClientStreamFlags.AutoConvertPcm | AudioClientStreamFlags.SrcDefaultQuality;
                    var sessionGuid = Guid.Empty;
                    hr = audioClient.Initialize(AUDCLNT_SHAREMODE.AUDCLNT_SHAREMODE_SHARED, streamFlags, hnsBufferDuration, 0, pFormat, ref sessionGuid);
                    if (hr != 0)
                    {
                        // Fallback without autoconvert
                        hr = audioClient.Initialize(AUDCLNT_SHAREMODE.AUDCLNT_SHAREMODE_SHARED, 0, hnsBufferDuration, 0, pFormat, ref sessionGuid);
                    }

                    if (hr != 0)
                    {
                        Marshal.ReleaseComObject(audioClient);
                        Marshal.ReleaseComObject(dev);
                        Thread.Sleep(200);
                        continue;
                    }

                    hr = audioClient.GetBufferSize(out uint bufferFrameCount);
                    if (hr != 0 || bufferFrameCount == 0)
                    {
                        Marshal.ReleaseComObject(audioClient);
                        Marshal.ReleaseComObject(dev);
                        Thread.Sleep(200);
                        continue;
                    }

                    var iidRender = IID_IAudioRenderClient;
                    hr = audioClient.GetService(ref iidRender, out IntPtr pRenderService);
                    if (hr != 0 || pRenderService == IntPtr.Zero)
                    {
                        Marshal.ReleaseComObject(audioClient);
                        Marshal.ReleaseComObject(dev);
                        Thread.Sleep(200);
                        continue;
                    }

                    var renderClient = (IAudioRenderClient)Marshal.GetObjectForIUnknown(pRenderService);
                    Marshal.Release(pRenderService);

                    audioClient.Start();

                    return new ActiveRenderClient(deviceId, dev, audioClient, renderClient, bufferFrameCount);
                }
                catch
                {
                    Thread.Sleep(200);
                }
            }

            return null;
        }

        private void SyncSecondaryRenders(
            IMMDeviceEnumerator enumerator,
            List<ActiveRenderClient> secondaryRenders,
            List<string> targetSecondaryIds,
            IntPtr pFormat,
            long hnsBufferDuration,
            CancellationToken token)
        {
            // Remove renders no longer in list
            for (int i = secondaryRenders.Count - 1; i >= 0; i--)
            {
                var r = secondaryRenders[i];
                if (!targetSecondaryIds.Contains(r.DeviceId, StringComparer.OrdinalIgnoreCase))
                {
                    r.Dispose();
                    secondaryRenders.RemoveAt(i);
                }
            }

            // Add new renders
            foreach (var targetId in targetSecondaryIds)
            {
                if (!secondaryRenders.Any(r => string.Equals(r.DeviceId, targetId, StringComparison.OrdinalIgnoreCase)))
                {
                    var client = CreateRenderClientWithRetry(enumerator, targetId, pFormat, hnsBufferDuration, token);
                    if (client != null)
                    {
                        secondaryRenders.Add(client);
                    }
                }
            }
        }

        public void Dispose()
        {
            StopBridge();
            _deviceManager.DevicesUpdated -= OnDevicesUpdated;
        }
    }
}
