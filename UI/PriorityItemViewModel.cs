using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using AudioSwitcher.CoreAudio;
using MediaBrush = System.Windows.Media.Brush;
using MediaColor = System.Windows.Media.Color;

namespace AudioSwitcher.UI
{
    public class PriorityItemViewModel : INotifyPropertyChanged
    {
        private int _rank;
        private string _id = string.Empty;
        private string _name = string.Empty;
        private bool _isConnected;
        private string _statusText = "Offline";
        private bool _isCurrentDefault;
        private bool _isTemporaryOverride;
        private string _otherCategoryBadge = string.Empty;
        private int _volumePercent = 100;
        private bool _isMuted = false;
        private bool _hasVolumeControl = false;

        public int Rank
        {
            get => _rank;
            set
            {
                if (_rank != value)
                {
                    _rank = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(RankBadgeText));
                    OnPropertyChanged(nameof(IsTopPriority));
                    OnPropertyChanged(nameof(CanMoveUp));
                }
            }
        }

        public string RankBadgeText => Rank == 1 ? "1 (Top)" : Rank.ToString();
        public bool IsTopPriority => Rank == 1;
        public bool CanMoveUp => Rank > 1;

        public string Id
        {
            get => _id;
            set { if (_id != value) { _id = value; OnPropertyChanged(); } }
        }

        public string Name
        {
            get => _name;
            set { if (_name != value) { _name = value; OnPropertyChanged(); } }
        }

        public bool IsConnected
        {
            get => _isConnected;
            set
            {
                if (_isConnected != value)
                {
                    _isConnected = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CardBackground));
                    OnPropertyChanged(nameof(CardBorderBrush));
                    OnPropertyChanged(nameof(CardBorderThickness));
                    OnPropertyChanged(nameof(CardOpacity));
                    OnPropertyChanged(nameof(IndicatorStripeBrush));
                    OnPropertyChanged(nameof(StatusDotBrush));
                    OnPropertyChanged(nameof(StatusDotForeground));
                    OnPropertyChanged(nameof(CanTempSwitch));
                }
            }
        }

        public string StatusText
        {
            get => _statusText;
            set { if (_statusText != value) { _statusText = value; OnPropertyChanged(); } }
        }

        public bool IsCurrentDefault
        {
            get => _isCurrentDefault;
            set
            {
                if (_isCurrentDefault != value)
                {
                    _isCurrentDefault = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CardBackground));
                    OnPropertyChanged(nameof(CardBorderBrush));
                    OnPropertyChanged(nameof(CardBorderThickness));
                    OnPropertyChanged(nameof(IndicatorStripeBrush));
                    OnPropertyChanged(nameof(CanTempSwitch));
                }
            }
        }

        public bool IsTemporaryOverride
        {
            get => _isTemporaryOverride;
            set
            {
                if (_isTemporaryOverride != value)
                {
                    _isTemporaryOverride = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CardBackground));
                    OnPropertyChanged(nameof(CardBorderBrush));
                    OnPropertyChanged(nameof(IndicatorStripeBrush));
                }
            }
        }

        public string OtherCategoryBadge
        {
            get => _otherCategoryBadge;
            set
            {
                if (_otherCategoryBadge != value)
                {
                    _otherCategoryBadge = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HasOtherCategoryBadge));
                }
            }
        }

        public bool HasOtherCategoryBadge => !string.IsNullOrWhiteSpace(OtherCategoryBadge);

        public bool CanTempSwitch => IsConnected && !IsCurrentDefault;

        // Volume properties
        public int VolumePercent
        {
            get => _volumePercent;
            set
            {
                int clamped = Math.Clamp(value, 0, 100);
                if (_volumePercent != clamped)
                {
                    _volumePercent = clamped;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(VolumeText));
                }
            }
        }

        public string VolumeText => $"{VolumePercent}%";

        public bool IsMuted
        {
            get => _isMuted;
            set
            {
                if (_isMuted != value)
                {
                    _isMuted = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(MuteIcon));
                }
            }
        }

        public string MuteIcon => IsMuted ? "🔇" : "🔊";

        public bool HasVolumeControl
        {
            get => _hasVolumeControl;
            set
            {
                if (_hasVolumeControl != value)
                {
                    _hasVolumeControl = value;
                    OnPropertyChanged();
                }
            }
        }

        // Visual distinction styling properties

        public MediaBrush CardBackground
        {
            get
            {
                if (IsCurrentDefault && IsTemporaryOverride)
                    return new SolidColorBrush(MediaColor.FromRgb(42, 27, 14)); // #2A1B0E Warm amber tint
                if (IsCurrentDefault)
                    return new SolidColorBrush(MediaColor.FromRgb(21, 34, 56)); // #152238 Sapphire blue tint
                if (IsConnected)
                    return new SolidColorBrush(MediaColor.FromRgb(36, 36, 45)); // #24242D Dark slate card
                return new SolidColorBrush(MediaColor.FromRgb(24, 24, 31)); // #18181F Subdued offline card
            }
        }

        public MediaBrush CardBorderBrush
        {
            get
            {
                if (IsCurrentDefault && IsTemporaryOverride)
                    return new SolidColorBrush(MediaColor.FromRgb(245, 158, 11)); // #F59E0B Amber
                if (IsCurrentDefault)
                    return new SolidColorBrush(MediaColor.FromRgb(59, 130, 246)); // #3B82F6 Royal blue
                if (IsConnected)
                    return new SolidColorBrush(MediaColor.FromRgb(54, 54, 68)); // #363644 Crisp border
                return new SolidColorBrush(MediaColor.FromRgb(38, 38, 48)); // #262630 Muted border
            }
        }

        public System.Windows.Thickness CardBorderThickness =>
            IsCurrentDefault ? new System.Windows.Thickness(1.5) : new System.Windows.Thickness(1);

        public double CardOpacity => IsConnected ? 1.0 : 0.58;

        public MediaBrush IndicatorStripeBrush
        {
            get
            {
                if (IsCurrentDefault && IsTemporaryOverride)
                    return new SolidColorBrush(MediaColor.FromRgb(245, 158, 11)); // Amber
                if (IsCurrentDefault)
                    return new SolidColorBrush(MediaColor.FromRgb(59, 130, 246)); // Blue
                if (IsConnected)
                    return new SolidColorBrush(MediaColor.FromRgb(16, 185, 129)); // Emerald green
                return new SolidColorBrush(MediaColor.FromRgb(75, 85, 99)); // Slate gray
            }
        }

        public MediaBrush StatusDotBrush => IsConnected
            ? new SolidColorBrush(MediaColor.FromRgb(34, 197, 94)) // #22C55E
            : new SolidColorBrush(MediaColor.FromRgb(156, 163, 175)); // #9CA3AF

        public MediaBrush StatusDotForeground => IsConnected
            ? new SolidColorBrush(MediaColor.FromRgb(74, 222, 128)) // #4ADE80
            : new SolidColorBrush(MediaColor.FromRgb(113, 113, 122)); // #71717A

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
