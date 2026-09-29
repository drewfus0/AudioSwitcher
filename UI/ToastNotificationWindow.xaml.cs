using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace AudioSwitcher.UI
{
    public partial class ToastNotificationWindow : Window
    {
        private static ToastNotificationWindow? _activeToast;
        private readonly DispatcherTimer _closeTimer;

        public ToastNotificationWindow(string title, string message, string icon, string? volumeBadge, bool isTemporaryOverride)
        {
            InitializeComponent();

            TxtTitle.Text = title;
            TxtMessage.Text = message;
            TxtIcon.Text = icon;

            if (!string.IsNullOrWhiteSpace(volumeBadge))
            {
                TxtVolumeBadge.Text = volumeBadge;
                BdrVolumeBadge.Visibility = Visibility.Visible;
            }
            else
            {
                BdrVolumeBadge.Visibility = Visibility.Collapsed;
            }

            if (isTemporaryOverride)
            {
                ToastBorder.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(245, 158, 11)); // Amber
                TxtTitle.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(251, 191, 36));
                BdrVolumeBadge.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(120, 53, 15));
                TxtVolumeBadge.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(253, 230, 138));
            }

            PositionAtBottomRight();

            _closeTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3.2)
            };
            _closeTimer.Tick += (s, e) =>
            {
                _closeTimer.Stop();
                FadeOutAndClose();
            };

            Loaded += (s, e) =>
            {
                var fadeIn = (Storyboard)Resources["FadeInStoryboard"];
                fadeIn.Begin(ToastBorder);
                _closeTimer.Start();
            };
        }

        private void PositionAtBottomRight()
        {
            var workArea = SystemParameters.WorkArea;
            Left = workArea.Right - Width - 16;
            Top = workArea.Bottom - Height - 16;
        }

        private void FadeOutAndClose()
        {
            var fadeOut = (Storyboard)Resources["FadeOutStoryboard"];
            fadeOut.Completed += (s, e) =>
            {
                try
                {
                    Close();
                }
                catch { }
            };
            fadeOut.Begin(ToastBorder);
        }

        private void BtnDismiss_Click(object sender, RoutedEventArgs e)
        {
            _closeTimer.Stop();
            try { Close(); } catch { }
        }

        private void Toast_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _closeTimer.Stop();
            try { Close(); } catch { }
            App.CurrentApp.ShowMainWindow();
        }

        public static void ShowToast(string title, string message, string icon = "🔊", string? volumeBadge = null, bool isTemporaryOverride = false)
        {
            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    if (_activeToast != null)
                    {
                        try { _activeToast.Close(); } catch { }
                        _activeToast = null;
                    }

                    _activeToast = new ToastNotificationWindow(title, message, icon, volumeBadge, isTemporaryOverride);
                    _activeToast.Show();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to display toast: {ex.Message}");
                }
            }));
        }
    }
}
