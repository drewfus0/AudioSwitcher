using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AudioSwitcher.CoreAudio
{
    public class AudioDevice : INotifyPropertyChanged
    {
        private string _id = string.Empty;
        private string _name = string.Empty;
        private string _deviceDesc = string.Empty;
        private EDataFlow _dataFlow;
        private EDeviceState _state;
        private bool _isDefaultSound;
        private bool _isDefaultCommunications;

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

        public string DeviceDesc
        {
            get => _deviceDesc;
            set { if (_deviceDesc != value) { _deviceDesc = value; OnPropertyChanged(); } }
        }

        public EDataFlow DataFlow
        {
            get => _dataFlow;
            set { if (_dataFlow != value) { _dataFlow = value; OnPropertyChanged(); } }
        }

        public EDeviceState State
        {
            get => _state;
            set
            {
                if (_state != value)
                {
                    _state = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsActive));
                    OnPropertyChanged(nameof(StatusText));
                }
            }
        }

        public bool IsActive => State == EDeviceState.DEVICE_STATE_ACTIVE;

        public string StatusText => State switch
        {
            EDeviceState.DEVICE_STATE_ACTIVE => "Connected",
            EDeviceState.DEVICE_STATE_DISABLED => "Disabled",
            EDeviceState.DEVICE_STATE_NOTPRESENT => "Not Present",
            EDeviceState.DEVICE_STATE_UNPLUGGED => "Unplugged",
            _ => "Unknown"
        };

        public bool IsDefaultSound
        {
            get => _isDefaultSound;
            set { if (_isDefaultSound != value) { _isDefaultSound = value; OnPropertyChanged(); } }
        }

        public bool IsDefaultCommunications
        {
            get => _isDefaultCommunications;
            set { if (_isDefaultCommunications != value) { _isDefaultCommunications = value; OnPropertyChanged(); } }
        }

        public bool IsDefault(AudioCategory category) => category switch
        {
            AudioCategory.OutputSound => IsDefaultSound && DataFlow == EDataFlow.eRender,
            AudioCategory.OutputCommunications => IsDefaultCommunications && DataFlow == EDataFlow.eRender,
            AudioCategory.InputSound => IsDefaultSound && DataFlow == EDataFlow.eCapture,
            AudioCategory.InputCommunications => IsDefaultCommunications && DataFlow == EDataFlow.eCapture,
            _ => false
        };

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public override string ToString() => $"{Name} ({StatusText})";
    }
}
