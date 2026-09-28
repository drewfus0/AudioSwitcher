using System.ComponentModel;
using System.Runtime.CompilerServices;
using AudioSwitcher.CoreAudio;

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
                }
            }
        }

        public string RankBadgeText => Rank == 1 ? "1 (Top)" : Rank.ToString();
        public bool IsTopPriority => Rank == 1;

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
            set { if (_isConnected != value) { _isConnected = value; OnPropertyChanged(); } }
        }

        public string StatusText
        {
            get => _statusText;
            set { if (_statusText != value) { _statusText = value; OnPropertyChanged(); } }
        }

        public bool IsCurrentDefault
        {
            get => _isCurrentDefault;
            set { if (_isCurrentDefault != value) { _isCurrentDefault = value; OnPropertyChanged(); } }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
