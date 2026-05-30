using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using AerialViews.Models;
using AerialViews.Services;
using LibVLCSharp.Shared;

namespace AerialViews.UI
{
    /// <summary>
    /// Lightweight screensaver window for secondary monitors.
    /// Plays the same video as the primary screen but without text overlays or progress bar.
    /// Uses its own LibVLC instance and OverlayWindow for fade/brightness effects.
    /// </summary>
    public partial class SecondaryScreensaverWindow : Window
    {
        private LibVLC? _libVLC;
        private LibVLCSharp.Shared.MediaPlayer? _mediaPlayer;
        private OverlayWindow? _overlayWindow;
        private bool _isClosing;

        /// <summary>
        /// Physical pixel bounds of the target screen. Set before Show().
        /// Used for precise Win32 positioning that bypasses WPF DPI scaling.
        /// </summary>
        public System.Drawing.Rectangle ScreenBounds { get; set; }

        public LibVLCSharp.Shared.MediaPlayer? MediaPlayer => _mediaPlayer;

        public SecondaryScreensaverWindow()
        {
            InitializeComponent();
            DataContext = this;
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // Position precisely on the target screen using Win32 (bypasses DPI issues)
            var hwnd = new WindowInteropHelper(this).Handle;
            MultiMonitorManager.SetWindowPosition(hwnd, ScreenBounds);

            // Initialize LibVLC (Core.Initialize is idempotent)
            Core.Initialize();
            _libVLC = new LibVLC();
            _mediaPlayer = new LibVLCSharp.Shared.MediaPlayer(_libVLC);
            _mediaPlayer.Mute = true; // Always mute secondary screens to avoid audio echo

            VideoView.MediaPlayer = _mediaPlayer;

            // Create overlay window for fade and brightness effects (no text overlays)
            _overlayWindow = new OverlayWindow();
            _overlayWindow.Left = ScreenBounds.Left;
            _overlayWindow.Top = ScreenBounds.Top;
            _overlayWindow.Width = ScreenBounds.Width;
            _overlayWindow.Height = ScreenBounds.Height;

            // Hide text overlays and progress bar — secondary screens show video only
            _overlayWindow.OverlayGrid.Visibility = Visibility.Collapsed;
            _overlayWindow.VideoProgressBar.Visibility = Visibility.Collapsed;

            _overlayWindow.Loaded += (s, args) =>
            {
                var overlayHwnd = new WindowInteropHelper(_overlayWindow).Handle;
                MultiMonitorManager.SetWindowPosition(overlayHwnd, ScreenBounds);
            };

            _overlayWindow.Show();
        }

        /// <summary>
        /// Plays the specified media with the same settings as the primary screen.
        /// </summary>
        public void PlayMedia(AerialMedia media, ScreensaverSettings settings, int? randomStartTime)
        {
            if (_libVLC == null || _mediaPlayer == null || _isClosing) return;

            // Reset fade overlay to black (opaque) while loading
            if (_overlayWindow != null)
            {
                _overlayWindow.FadeOverlay.BeginAnimation(
                    UIElement.OpacityProperty, null);
                _overlayWindow.FadeOverlay.Opacity = 1;
            }

            try
            {
                var resolvedUri = VideoCacheManager.ResolvePlaybackUri(media.Uri, media.Source);
                var vlcMedia = resolvedUri.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    ? new Media(_libVLC, new Uri(resolvedUri))
                    : new Media(_libVLC, resolvedUri);

                // Apply same options as primary
                if (settings.Playlist.MaxVideoLengthSeconds > 0)
                    vlcMedia.AddOption($":stop-time={settings.Playlist.MaxVideoLengthSeconds}");

                // Use the same random start time as primary (for sync)
                if (randomStartTime != null)
                    vlcMedia.AddOption($":start-time={randomStartTime}");

                _mediaPlayer.SetRate((float)settings.Playlist.PlaybackSpeed);
                _mediaPlayer.Play(vlcMedia);
            }
            catch { }
        }

        /// <summary>
        /// Stops current video playback.
        /// </summary>
        public void StopPlayback()
        {
            if (_mediaPlayer == null || _isClosing) return;

            // VLC Stop must run off UI thread to avoid deadlock
            var mp = _mediaPlayer;
            Task.Run(() => { try { mp?.Stop(); } catch { } });
        }

        /// <summary>
        /// Fades in the video by hiding the black overlay.
        /// </summary>
        public void FadeIn(int durationMs)
        {
            if (_overlayWindow == null || _isClosing) return;
            var fadeIn = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(durationMs));
            _overlayWindow.FadeOverlay.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        }

        /// <summary>
        /// Fades out the video by showing the black overlay.
        /// </summary>
        public void FadeOut(int durationMs)
        {
            if (_overlayWindow == null || _isClosing) return;
            var fadeOut = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(durationMs));
            _overlayWindow.FadeOverlay.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        }

        /// <summary>
        /// Applies appearance settings (brightness, gradients) to the overlay.
        /// </summary>
        public void ApplyAppearance(ScreensaverSettings settings)
        {
            if (_overlayWindow == null) return;

            if (settings.Appearance.VideoBrightness < 100)
                _overlayWindow.BrightnessOverlay.Opacity = 1.0 - (settings.Appearance.VideoBrightness / 100.0);

            _overlayWindow.TopGradient.Visibility = settings.Appearance.ShowTopGradient
                ? Visibility.Visible : Visibility.Collapsed;
            _overlayWindow.BottomGradient.Visibility = settings.Appearance.ShowBottomGradient
                ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>
        /// Closes this window and cleans up VLC resources.
        /// </summary>
        public void CloseScreensaver()
        {
            if (_isClosing) return;
            _isClosing = true;

            _overlayWindow?.Close();

            var mp = _mediaPlayer;
            var vlc = _libVLC;
            _mediaPlayer = null;
            _libVLC = null;

            Task.Run(() =>
            {
                try { mp?.Stop(); mp?.Dispose(); vlc?.Dispose(); } catch { }
            });

            Close();
        }

        // User input on any screen closes all screensaver windows
        private void Window_KeyDown(object sender, KeyEventArgs e) => MultiMonitorManager.CloseAll();
        private void Window_MouseDown(object sender, MouseButtonEventArgs e) => MultiMonitorManager.CloseAll();
    }
}
