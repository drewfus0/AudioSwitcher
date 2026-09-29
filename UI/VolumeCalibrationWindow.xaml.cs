using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using AudioSwitcher.CoreAudio;
using AudioSwitcher.Services;
using MediaColor = System.Windows.Media.Color;
using WpfPoint = System.Windows.Point;

namespace AudioSwitcher.UI
{
    public partial class VolumeCalibrationWindow : Window
    {
        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        private readonly WasapiTestTonePlayer _player = WasapiTestTonePlayer.Instance;
        private DeviceVolumeProfile _currentProfile = new();
        private double _activeRefLevel = 0.50; // 0.25, 0.50, or 0.75
        private bool _isUpdatingUi = true;

        public VolumeCalibrationWindow()
        {
            _isUpdatingUi = true;
            InitializeComponent();

            _player.ActivePlaybackChanged += OnActivePlaybackChanged;
            Loaded += VolumeCalibrationWindow_Loaded;
            Closed += VolumeCalibrationWindow_Closed;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            try
            {
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                int useDarkMode = 1;
                if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDarkMode, sizeof(int)) != 0)
                {
                    DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref useDarkMode, sizeof(int));
                }
            }
            catch { }
        }

        private void VolumeCalibrationWindow_Loaded(object sender, RoutedEventArgs e)
        {
            _isUpdatingUi = true;
            try
            {
                if (ChkEnableVolumeMapping != null)
                {
                    ChkEnableVolumeMapping.IsChecked = SettingsService.Instance.Settings.EnableVolumeMappingOnSwitch;
                }

                if (RbLevelMid != null)
                {
                    RbLevelMid.IsChecked = true;
                }

                if (CmbSignalType != null && CmbSignalType.SelectedIndex < 0)
                {
                    CmbSignalType.SelectedIndex = 0;
                }

                PopulateDeviceCombos();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"VolumeCalibrationWindow_Loaded error: {ex.Message}");
            }
            finally
            {
                _isUpdatingUi = false;
            }

            LoadProfileForSelectedTarget();
        }

        private void VolumeCalibrationWindow_Closed(object? sender, EventArgs e)
        {
            try
            {
                _player.Stop();
                _player.ActivePlaybackChanged -= OnActivePlaybackChanged;
            }
            catch { }
        }

        private void PopulateDeviceCombos()
        {
            if (CmbReferenceDevice == null || CmbTargetDevice == null) return;

            bool wasUpdating = _isUpdatingUi;
            _isUpdatingUi = true;
            try
            {
                var devices = AudioDeviceManager.Instance.GetDevices(EDataFlow.eRender)
                    .Where(d => d.IsActive && !SettingsService.Instance.IsDeviceIgnored(d.Id, d.Name))
                    .ToList();

                CmbReferenceDevice.Items.Clear();
                CmbTargetDevice.Items.Clear();

                foreach (var d in devices)
                {
                    CmbReferenceDevice.Items.Add(new ComboBoxItem { Content = d.Name, Tag = d });
                    CmbTargetDevice.Items.Add(new ComboBoxItem { Content = d.Name, Tag = d });
                }

                if (devices.Count > 0)
                {
                    // Pick default sound as reference
                    int refIndex = devices.FindIndex(d => d.IsDefaultSound);
                    CmbReferenceDevice.SelectedIndex = refIndex >= 0 ? refIndex : 0;

                    // Pick another device as target if available
                    int targetIndex = devices.Count > 1 ? (refIndex == 0 ? 1 : 0) : 0;
                    CmbTargetDevice.SelectedIndex = targetIndex;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"PopulateDeviceCombos error: {ex.Message}");
            }
            finally
            {
                _isUpdatingUi = wasUpdating;
            }
        }

        private void LoadProfileForSelectedTarget()
        {
            try
            {
                var targetDev = GetSelectedTargetDevice();
                if (targetDev != null)
                {
                    _currentProfile = SettingsService.Instance.GetOrCreateVolumeProfile(targetDev.Id, targetDev.Name);
                }
                else
                {
                    _currentProfile = new DeviceVolumeProfile("default", "Default");
                }

                UpdateUiForActiveLevel();
                RedrawCurveGraph();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LoadProfileForSelectedTarget error: {ex.Message}");
            }
        }

        private AudioDevice? GetSelectedReferenceDevice()
        {
            if (CmbReferenceDevice == null) return null;
            return (CmbReferenceDevice.SelectedItem as ComboBoxItem)?.Tag as AudioDevice;
        }

        private AudioDevice? GetSelectedTargetDevice()
        {
            if (CmbTargetDevice == null) return null;
            return (CmbTargetDevice.SelectedItem as ComboBoxItem)?.Tag as AudioDevice;
        }

        private TestSignalType GetSelectedSignalType()
        {
            if (CmbSignalType == null) return TestSignalType.PinkNoise;
            int tag = int.TryParse((CmbSignalType.SelectedItem as ComboBoxItem)?.Tag?.ToString(), out int val) ? val : 0;
            return (TestSignalType)tag;
        }

        private void UpdateUiForActiveLevel()
        {
            if (SliderTargetVolume == null || TxtTargetVolumePercent == null) return;

            try
            {
                double targetVol = VolumeMappingService.ForwardMap(_currentProfile, _activeRefLevel);
                int percent = (int)Math.Round(targetVol * 100.0);

                bool wasUpdating = _isUpdatingUi;
                _isUpdatingUi = true;
                try
                {
                    SliderTargetVolume.Value = percent;
                    TxtTargetVolumePercent.Text = $"{percent}%";
                }
                finally
                {
                    _isUpdatingUi = wasUpdating;
                }

                UpdatePointsSummaryText();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UpdateUiForActiveLevel error: {ex.Message}");
            }
        }

        private void UpdatePointsSummaryText()
        {
            if (TxtPointLow == null || TxtPointMid == null || TxtPointHigh == null) return;
            if (_currentProfile == null) return;

            try
            {
                double pLow = VolumeMappingService.ForwardMap(_currentProfile, 0.25) * 100.0;
                double pMid = VolumeMappingService.ForwardMap(_currentProfile, 0.50) * 100.0;
                double pHigh = VolumeMappingService.ForwardMap(_currentProfile, 0.75) * 100.0;

                TxtPointLow.Text = $"• Low (25% Ref) ➔ Target: {Math.Round(pLow)}%";
                TxtPointMid.Text = $"• Mid (50% Ref) ➔ Target: {Math.Round(pMid)}%";
                TxtPointHigh.Text = $"• High (75% Ref) ➔ Target: {Math.Round(pHigh)}%";
            }
            catch { }
        }

        private void OnActivePlaybackChanged(string? activeDeviceId)
        {
            Dispatcher.Invoke(() =>
            {
                if (BdrActivePlayingBadge == null || TxtActivePlayingBadge == null) return;

                if (string.IsNullOrEmpty(activeDeviceId))
                {
                    BdrActivePlayingBadge.Background = new SolidColorBrush(MediaColor.FromRgb(30, 41, 59));
                    BdrActivePlayingBadge.BorderBrush = new SolidColorBrush(MediaColor.FromRgb(59, 130, 246));
                    TxtActivePlayingBadge.Text = "⏹ Audio Stopped. Click buttons above to test.";
                    TxtActivePlayingBadge.Foreground = new SolidColorBrush(MediaColor.FromRgb(147, 197, 253));
                    return;
                }

                var refDev = GetSelectedReferenceDevice();
                var targetDev = GetSelectedTargetDevice();

                if (refDev != null && string.Equals(activeDeviceId, refDev.Id, StringComparison.OrdinalIgnoreCase))
                {
                    BdrActivePlayingBadge.Background = new SolidColorBrush(MediaColor.FromRgb(15, 35, 60));
                    BdrActivePlayingBadge.BorderBrush = new SolidColorBrush(MediaColor.FromRgb(59, 130, 246));
                    TxtActivePlayingBadge.Text = $"🔊 PLAYING REFERENCE ({refDev.Name}) at {(int)(_activeRefLevel * 100)}%";
                    TxtActivePlayingBadge.Foreground = new SolidColorBrush(MediaColor.FromRgb(96, 165, 250));
                }
                else if (targetDev != null && string.Equals(activeDeviceId, targetDev.Id, StringComparison.OrdinalIgnoreCase))
                {
                    int targetVal = SliderTargetVolume != null ? (int)SliderTargetVolume.Value : 50;
                    BdrActivePlayingBadge.Background = new SolidColorBrush(MediaColor.FromRgb(45, 28, 12));
                    BdrActivePlayingBadge.BorderBrush = new SolidColorBrush(MediaColor.FromRgb(245, 158, 11));
                    TxtActivePlayingBadge.Text = $"🎧 PLAYING TARGET ({targetDev.Name}) at {targetVal}%";
                    TxtActivePlayingBadge.Foreground = new SolidColorBrush(MediaColor.FromRgb(253, 230, 138));
                }
                else
                {
                    BdrActivePlayingBadge.Background = new SolidColorBrush(MediaColor.FromRgb(30, 58, 40));
                    BdrActivePlayingBadge.BorderBrush = new SolidColorBrush(MediaColor.FromRgb(34, 197, 94));
                    TxtActivePlayingBadge.Text = "🔊 PLAYING ON BOTH DEVICES (Dual Mode)";
                    TxtActivePlayingBadge.Foreground = new SolidColorBrush(MediaColor.FromRgb(134, 239, 172));
                }
            });
        }

        #region Event Handlers

        private void CmbReferenceDevice_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingUi) return;
            try
            {
                if (_player.IsPlaying) _player.Stop();
            }
            catch { }
        }

        private void CmbTargetDevice_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingUi) return;
            try
            {
                if (_player.IsPlaying) _player.Stop();
                LoadProfileForSelectedTarget();
            }
            catch { }
        }

        private void CmbSignalType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingUi) return;
            try
            {
                if (_player.IsPlaying)
                {
                    // Restart with new signal type
                    BtnAutoABToggle_Click(sender, e);
                }
            }
            catch { }
        }

        private void RbLevel_Checked(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingUi) return;
            if (RbLevelLow == null || RbLevelMid == null || RbLevelHigh == null) return;

            try
            {
                if (RbLevelLow.IsChecked == true) _activeRefLevel = 0.25;
                else if (RbLevelMid.IsChecked == true) _activeRefLevel = 0.50;
                else if (RbLevelHigh.IsChecked == true) _activeRefLevel = 0.75;

                // Apply reference volume to reference device
                var refDev = GetSelectedReferenceDevice();
                if (refDev != null)
                {
                    AudioVolumeManager.Instance.SetVolume(refDev.Id, (float)_activeRefLevel);
                }

                UpdateUiForActiveLevel();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"RbLevel_Checked error: {ex.Message}");
            }
        }

        private void SliderTargetVolume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtTargetVolumePercent != null && SliderTargetVolume != null)
            {
                TxtTargetVolumePercent.Text = $"{(int)SliderTargetVolume.Value}%";
            }

            if (_isUpdatingUi) return;

            try
            {
                var targetDev = GetSelectedTargetDevice();
                if (targetDev != null && SliderTargetVolume != null)
                {
                    float scalar = (float)(SliderTargetVolume.Value / 100.0);
                    AudioVolumeManager.Instance.SetVolume(targetDev.Id, scalar);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SliderTargetVolume_ValueChanged error: {ex.Message}");
            }
        }

        private void BtnStorePoint_Click(object sender, RoutedEventArgs e)
        {
            if (SliderTargetVolume == null || _currentProfile == null) return;

            try
            {
                double targetScalar = Math.Clamp(SliderTargetVolume.Value / 100.0, 0.0, 1.0);

                if (_currentProfile.Points == null) _currentProfile.Points = new List<VolumeCalibrationPoint>();

                var existing = _currentProfile.Points.FirstOrDefault(p => Math.Abs(p.RefVolume - _activeRefLevel) < 0.02);
                if (existing != null)
                {
                    existing.TargetVolume = targetScalar;
                }
                else
                {
                    _currentProfile.Points.Add(new VolumeCalibrationPoint(_activeRefLevel, targetScalar));
                }

                _currentProfile.Points = _currentProfile.Points.OrderBy(p => p.RefVolume).ToList();
                UpdatePointsSummaryText();
                RedrawCurveGraph();

                if (TxtStatusMessage != null)
                {
                    TxtStatusMessage.Text = $"Stored calibration point: Reference {(int)(_activeRefLevel * 100)}% ➔ Target {(int)(targetScalar * 100)}%";
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"BtnStorePoint_Click error: {ex.Message}");
            }
        }

        private void BtnPlayReference_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var refDev = GetSelectedReferenceDevice();
                if (refDev == null)
                {
                    if (TxtStatusMessage != null) TxtStatusMessage.Text = "Please select a Reference device.";
                    return;
                }

                AudioVolumeManager.Instance.SetVolume(refDev.Id, (float)_activeRefLevel);
                _player.StartSingle(refDev.Id, GetSelectedSignalType());
            }
            catch (Exception ex)
            {
                if (TxtStatusMessage != null) TxtStatusMessage.Text = $"Play Reference error: {ex.Message}";
            }
        }

        private void BtnPlayTarget_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var targetDev = GetSelectedTargetDevice();
                if (targetDev == null)
                {
                    if (TxtStatusMessage != null) TxtStatusMessage.Text = "Please select a Target device.";
                    return;
                }

                float scalar = SliderTargetVolume != null ? (float)(SliderTargetVolume.Value / 100.0) : 0.5f;
                AudioVolumeManager.Instance.SetVolume(targetDev.Id, scalar);
                _player.StartSingle(targetDev.Id, GetSelectedSignalType());
            }
            catch (Exception ex)
            {
                if (TxtStatusMessage != null) TxtStatusMessage.Text = $"Play Target error: {ex.Message}";
            }
        }

        private void BtnAutoABToggle_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var refDev = GetSelectedReferenceDevice();
                var targetDev = GetSelectedTargetDevice();
                if (refDev == null || targetDev == null)
                {
                    if (TxtStatusMessage != null) TxtStatusMessage.Text = "Please select both Reference and Target devices.";
                    return;
                }

                float targetScalar = SliderTargetVolume != null ? (float)(SliderTargetVolume.Value / 100.0) : 0.5f;
                AudioVolumeManager.Instance.SetVolume(refDev.Id, (float)_activeRefLevel);
                AudioVolumeManager.Instance.SetVolume(targetDev.Id, targetScalar);

                _player.StartAlternate(refDev.Id, targetDev.Id, GetSelectedSignalType(), 2000);
            }
            catch (Exception ex)
            {
                if (TxtStatusMessage != null) TxtStatusMessage.Text = $"Auto A-B Switch error: {ex.Message}";
            }
        }

        private void BtnStopAudio_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _player.Stop();
            }
            catch { }
        }

        private void BtnQuickProportional_Click(object sender, RoutedEventArgs e)
        {
            if (_currentProfile == null) return;

            try
            {
                // Calculate scale factor from 50% point
                double midTarget = VolumeMappingService.ForwardMap(_currentProfile, 0.50);
                double ratio = midTarget / 0.50;

                _currentProfile.Points = new List<VolumeCalibrationPoint>
                {
                    new(0.0, 0.0),
                    new(0.25, Math.Clamp(0.25 * ratio, 0.05, 0.95)),
                    new(0.50, midTarget),
                    new(0.75, Math.Clamp(0.75 * ratio, 0.10, 1.0)),
                    new(1.0, 1.0)
                };

                UpdateUiForActiveLevel();
                RedrawCurveGraph();
                if (TxtStatusMessage != null) TxtStatusMessage.Text = "Generated proportional curve from 50% Mid Point.";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"BtnQuickProportional_Click error: {ex.Message}");
            }
        }

        private void BtnResetLinear_Click(object sender, RoutedEventArgs e)
        {
            if (_currentProfile == null) return;

            try
            {
                _currentProfile.Points = new List<VolumeCalibrationPoint>
                {
                    new(0.0, 0.0),
                    new(0.25, 0.25),
                    new(0.50, 0.50),
                    new(0.75, 0.75),
                    new(1.0, 1.0)
                };

                UpdateUiForActiveLevel();
                RedrawCurveGraph();
                if (TxtStatusMessage != null) TxtStatusMessage.Text = "Reset curve to standard 1:1 linear mapping.";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"BtnResetLinear_Click error: {ex.Message}");
            }
        }

        private void BtnSaveProfile_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var targetDev = GetSelectedTargetDevice();
                if (targetDev != null && _currentProfile != null)
                {
                    _currentProfile.DeviceId = targetDev.Id;
                    _currentProfile.DeviceName = targetDev.Name;
                    SettingsService.Instance.SaveVolumeProfile(_currentProfile);
                    if (TxtStatusMessage != null) TxtStatusMessage.Text = $"Saved calibration profile for '{targetDev.Name}'.";
                }
            }
            catch (Exception ex)
            {
                if (TxtStatusMessage != null) TxtStatusMessage.Text = $"Failed to save profile: {ex.Message}";
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _player.Stop();
                Close();
            }
            catch { }
        }

        private void ChkEnableVolumeMapping_Changed(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingUi || ChkEnableVolumeMapping == null) return;

            try
            {
                SettingsService.Instance.Settings.EnableVolumeMappingOnSwitch = ChkEnableVolumeMapping.IsChecked == true;
                SettingsService.Instance.Save();
            }
            catch { }
        }

        #endregion

        #region Curve Canvas Visualizer

        private void CanvasCurveGraph_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            RedrawCurveGraph();
        }

        private void RedrawCurveGraph()
        {
            if (CanvasCurveGraph == null) return;

            try
            {
                CanvasCurveGraph.Children.Clear();

                double w = CanvasCurveGraph.ActualWidth;
                double h = CanvasCurveGraph.ActualHeight;
                if (w < 40 || h < 40 || _currentProfile == null || _currentProfile.Points == null) return;

                double pad = 16.0;
                double plotW = w - pad * 2.0;
                double plotH = h - pad * 2.0;

                // Draw grid lines
                for (int i = 0; i <= 4; i++)
                {
                    double fraction = i / 4.0;
                    double x = pad + fraction * plotW;
                    double y = pad + (1.0 - fraction) * plotH;

                    // Vertical grid line
                    var vLine = new Line
                    {
                        X1 = x, Y1 = pad,
                        X2 = x, Y2 = pad + plotH,
                        Stroke = new SolidColorBrush(MediaColor.FromRgb(38, 38, 48)),
                        StrokeThickness = 1
                    };
                    CanvasCurveGraph.Children.Add(vLine);

                    // Horizontal grid line
                    var hLine = new Line
                    {
                        X1 = pad, Y1 = y,
                        X2 = pad + plotW, Y2 = y,
                        Stroke = new SolidColorBrush(MediaColor.FromRgb(38, 38, 48)),
                        StrokeThickness = 1
                    };
                    CanvasCurveGraph.Children.Add(hLine);
                }

                // Draw 1:1 reference line (dashed diagonal)
                var refDiag = new Line
                {
                    X1 = pad, Y1 = pad + plotH,
                    X2 = pad + plotW, Y2 = pad,
                    Stroke = new SolidColorBrush(MediaColor.FromRgb(75, 85, 99)),
                    StrokeThickness = 1.5,
                    StrokeDashArray = new DoubleCollection { 4, 4 }
                };
                CanvasCurveGraph.Children.Add(refDiag);

                // Draw calibrated profile curve
                var polyline = new Polyline
                {
                    Stroke = new SolidColorBrush(MediaColor.FromRgb(59, 130, 246)),
                    StrokeThickness = 2.5
                };

                for (double step = 0; step <= 1.001; step += 0.02)
                {
                    double targetVal = VolumeMappingService.ForwardMap(_currentProfile, step);
                    double px = pad + step * plotW;
                    double py = pad + (1.0 - targetVal) * plotH;
                    polyline.Points.Add(new WpfPoint(px, py));
                }
                CanvasCurveGraph.Children.Add(polyline);

                // Draw calibrated points as circles
                foreach (var pt in _currentProfile.Points)
                {
                    double cx = pad + pt.RefVolume * plotW;
                    double cy = pad + (1.0 - pt.TargetVolume) * plotH;

                    bool isActive = Math.Abs(pt.RefVolume - _activeRefLevel) < 0.02;

                    var ellipse = new Ellipse
                    {
                        Width = isActive ? 12 : 8,
                        Height = isActive ? 12 : 8,
                        Fill = isActive
                            ? new SolidColorBrush(MediaColor.FromRgb(245, 158, 11)) // Amber for active
                            : new SolidColorBrush(MediaColor.FromRgb(96, 165, 250)),
                        Stroke = new SolidColorBrush(MediaColor.FromRgb(255, 255, 255)),
                        StrokeThickness = isActive ? 2 : 1
                    };

                    Canvas.SetLeft(ellipse, cx - ellipse.Width / 2.0);
                    Canvas.SetTop(ellipse, cy - ellipse.Height / 2.0);
                    CanvasCurveGraph.Children.Add(ellipse);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"RedrawCurveGraph error: {ex.Message}");
            }
        }

        #endregion
    }
}
