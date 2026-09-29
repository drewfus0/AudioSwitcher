using System;
using System.Collections.Generic;
using System.Linq;

namespace AudioSwitcher.Services
{
    public class VolumeMappingService
    {
        private static VolumeMappingService? _instance;
        public static VolumeMappingService Instance => _instance ??= new VolumeMappingService();

        private readonly SettingsService _settingsService;

        public VolumeMappingService()
        {
            _settingsService = SettingsService.Instance;
        }

        public double MapVolumeBetweenDevices(
            string fromDeviceId, string fromDeviceName,
            string toDeviceId, string toDeviceName,
            double fromVolumeScalar)
        {
            fromVolumeScalar = Math.Clamp(fromVolumeScalar, 0.0, 1.0);

            if (string.Equals(fromDeviceId, toDeviceId, StringComparison.OrdinalIgnoreCase))
            {
                return fromVolumeScalar;
            }

            var fromProfile = _settingsService.GetOrCreateVolumeProfile(fromDeviceId, fromDeviceName);
            var toProfile = _settingsService.GetOrCreateVolumeProfile(toDeviceId, toDeviceName);

            // Step 1: Inverse map from source device to virtual normalized master loudness (0.0 to 1.0)
            double normalizedLoudness = InverseMap(fromProfile, fromVolumeScalar);

            // Step 2: Forward map from virtual master loudness to target device hardware volume (0.0 to 1.0)
            double targetVolume = ForwardMap(toProfile, normalizedLoudness);

            return Math.Clamp(targetVolume, 0.0, 1.0);
        }

        public static double ForwardMap(DeviceVolumeProfile profile, double normalizedLoudness)
        {
            normalizedLoudness = Math.Clamp(normalizedLoudness, 0.0, 1.0);
            var points = GetCleanSortedPoints(profile.Points);

            if (points.Count == 0) return normalizedLoudness;
            if (normalizedLoudness <= points[0].RefVolume) return points[0].TargetVolume;
            if (normalizedLoudness >= points[^1].RefVolume) return points[^1].TargetVolume;

            for (int i = 0; i < points.Count - 1; i++)
            {
                var p1 = points[i];
                var p2 = points[i + 1];

                if (normalizedLoudness >= p1.RefVolume && normalizedLoudness <= p2.RefVolume)
                {
                    double dx = p2.RefVolume - p1.RefVolume;
                    if (Math.Abs(dx) < 0.0001) return p1.TargetVolume;

                    double t = (normalizedLoudness - p1.RefVolume) / dx;
                    return p1.TargetVolume + t * (p2.TargetVolume - p1.TargetVolume);
                }
            }

            return normalizedLoudness;
        }

        public static double InverseMap(DeviceVolumeProfile profile, double targetVolume)
        {
            targetVolume = Math.Clamp(targetVolume, 0.0, 1.0);
            var points = GetCleanSortedPoints(profile.Points);

            if (points.Count == 0) return targetVolume;
            if (targetVolume <= points[0].TargetVolume) return points[0].RefVolume;
            if (targetVolume >= points[^1].TargetVolume) return points[^1].RefVolume;

            for (int i = 0; i < points.Count - 1; i++)
            {
                var p1 = points[i];
                var p2 = points[i + 1];

                double minTarget = Math.Min(p1.TargetVolume, p2.TargetVolume);
                double maxTarget = Math.Max(p1.TargetVolume, p2.TargetVolume);

                if (targetVolume >= minTarget && targetVolume <= maxTarget)
                {
                    double dy = p2.TargetVolume - p1.TargetVolume;
                    if (Math.Abs(dy) < 0.0001) return p1.RefVolume;

                    double t = (targetVolume - p1.TargetVolume) / dy;
                    return p1.RefVolume + t * (p2.RefVolume - p1.RefVolume);
                }
            }

            return targetVolume;
        }

        private static List<VolumeCalibrationPoint> GetCleanSortedPoints(List<VolumeCalibrationPoint>? rawPoints)
        {
            if (rawPoints == null || rawPoints.Count == 0)
            {
                return new List<VolumeCalibrationPoint>
                {
                    new(0.0, 0.0),
                    new(0.25, 0.25),
                    new(0.50, 0.50),
                    new(0.75, 0.75),
                    new(1.0, 1.0)
                };
            }

            var list = rawPoints.OrderBy(p => p.RefVolume).ToList();

            if (list[0].RefVolume > 0.0001)
            {
                list.Insert(0, new VolumeCalibrationPoint(0.0, 0.0));
            }

            if (list[^1].RefVolume < 0.9999)
            {
                list.Add(new VolumeCalibrationPoint(1.0, 1.0));
            }

            return list;
        }
    }
}
