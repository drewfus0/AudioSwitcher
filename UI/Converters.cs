using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using AudioSwitcher.CoreAudio;
using MediaColor = System.Windows.Media.Color;

namespace AudioSwitcher.UI
{
    public class BoolToVisibilityConverter : IValueConverter
    {
        public bool Invert { get; set; } = false;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool b = value is bool flag && flag;
            if (Invert) b = !b;
            return b ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class StringNotEmptyToVisibilityConverter : IValueConverter
    {
        public bool Invert { get; set; } = false;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool hasValue = value is string str && !string.IsNullOrWhiteSpace(str);
            if (Invert) hasValue = !hasValue;
            return hasValue ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class ConnectionStatusBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isActive && isActive)
            {
                return new SolidColorBrush(MediaColor.FromRgb(34, 197, 94)); // Vibrant green #22C55E
            }
            return new SolidColorBrush(MediaColor.FromRgb(107, 114, 128)); // Slate gray #6B7280
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class CategoryHeaderBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is AudioCategory category)
            {
                return category switch
                {
                    AudioCategory.OutputSound => new SolidColorBrush(MediaColor.FromRgb(59, 130, 246)), // Blue
                    AudioCategory.OutputCommunications => new SolidColorBrush(MediaColor.FromRgb(168, 85, 247)), // Purple
                    AudioCategory.InputSound => new SolidColorBrush(MediaColor.FromRgb(16, 185, 129)), // Emerald
                    AudioCategory.InputCommunications => new SolidColorBrush(MediaColor.FromRgb(249, 115, 22)), // Amber/Orange
                    _ => new SolidColorBrush(MediaColor.FromRgb(59, 130, 246))
                };
            }
            return new SolidColorBrush(MediaColor.FromRgb(59, 130, 246));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
