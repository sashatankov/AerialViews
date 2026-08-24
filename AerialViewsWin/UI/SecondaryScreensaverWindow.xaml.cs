using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using AerialViews.Models;
using AerialViews.Services;
using LibVLCSharp.Shared;

namespace AerialViews.UI
{
    /// <summary>
    /// Lightweight screensaver window for secondary monitors.
    /// Plays the same video as the primary screen but without text overlays or progress bar.
    /// Uses its own LibVLC instance and a VideoView-hosted overlay for fade/brightness effects.
    /// </summary>
    public partial class SecondaryScreensaverWindow : Window
    {
        private LibVLC? _libVLC;
        private LibVLCSharp.Shared.MediaPlayer? _mediaPlayer;
        private readonly VideoOverlay _overlayWindow;
        private Media? _currentVlcMedia;
        private readonly object _playerLock = new();
        private bool _isClosing;
        private int _playToken;
        private bool _firstFrameReady;
        private bool _fadeInRequested;
        private int _fadeInDurationMs;
        private EventHandler<MediaPlayerTimeChangedEventArgs>? _firstFrameHandler;

        /// <summary>
        /// Physical pixel bounds of the target screen. Set before Show().
        /// Used for precise Win32 positioning that bypasses WPF DPI scaling.
        /// </summary>
        public System.Drawing.Rectangle ScreenBounds { get; set; }

        public LibVLCSharp.Shared.MediaPlayer? MediaPlayer => _mediaPlayer;

        public SecondaryScreensaverWindow()
        {
            InitializeComponent();
            _overlayWindow = Overlay;
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

            // Hide text overlays and progress bar — secondary screens show video only
            _overlayWindow.OverlayGrid.Visibility = Visibility.Collapsed;
            _overlayWindow.VideoProgressBar.Visibility = Visibility.Collapsed;
            _overlayWindow.LoadingText.Visibility = Visibility.Collapsed;
            _overlayWindow.SetBlack();
        }

        /// <summary>
        /// Plays the specified media with the same settings as the primary screen.
        /// </summary>
        public void PlayMedia(AerialMedia media, ScreensaverSettings settings, int? randomStartTime)
        {
            if (_libVLC == null || _mediaPlayer == null || _isClosing) return;

            var token = ++_playToken;
            _firstFrameReady = false;
            _fadeInRequested = false;
            _overlayWindow.SetBlack();

            if (_firstFrameHandler != null)
            {
                _mediaPlayer.TimeChanged -= _firstFrameHandler;
                _firstFrameHandler = null;
            }

            try
            {
                var resolvedUri = VideoCacheManager.ResolvePlaybackUri(media.Uri, media.Source);
                var vlcMedia = resolvedUri.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    ? new Media(_libVLC, new Uri(resolvedUri))
                    : new Media(_libVLC, resolvedUri);

                // Apply same options as primary: max length is enforced by the
                // primary's pre-emptive fade (no :stop-time — see primary), and
                // :stop-time only bounds the loop segment in loop mode.
                if (settings.Playlist.MaxVideoLengthSeconds > 0 && settings.Playlist.LoopUntilSkipped)
                    vlcMedia.AddOption($":stop-time={settings.Playlist.MaxVideoLengthSeconds}");

                // Use the same random start time as primary (for sync)
                if (randomStartTime != null)
                    vlcMedia.AddOption($":start-time={randomStartTime}");

                // Loop natively inside VLC, same as the primary screen
                if (settings.Playlist.LoopUntilSkipped)
                    vlcMedia.AddOption(":input-repeat=65535");

                _currentVlcMedia = vlcMedia;

                var mp = _mediaPlayer;
                EventHandler<MediaPlayerTimeChangedEventArgs>? firstFrameHandler = null;
                firstFrameHandler = (s, e) =>
                {
                    if (firstFrameHandler != null)
                        mp.TimeChanged -= firstFrameHandler;

                    Dispatcher.BeginInvoke(() => MarkFirstFrameReady(token, firstFrameHandler));
                };
                _firstFrameHandler = firstFrameHandler;
                mp.TimeChanged += firstFrameHandler;

                mp.SetRate((float)settings.Playlist.PlaybackSpeed);
                mp.Play(vlcMedia);
            }
            catch { }
        }

        private void MarkFirstFrameReady(
            int token,
            EventHandler<MediaPlayerTimeChangedEventArgs>? handler)
        {
            if (_firstFrameHandler == handler)
                _firstFrameHandler = null;

            if (_isClosing || token != _playToken) return;

            _firstFrameReady = true;
            if (_fadeInRequested)
            {
                _fadeInRequested = false;
                _overlayWindow.FadeFromBlack(_fadeInDurationMs);
            }
        }

        /// <summary>
        /// Stops current video playback off the UI thread (VLC Stop blocks on
        /// VLC threads that need the UI message pump). The primary window
        /// awaits the returned task before starting the next video, so a
        /// pending Stop can never race the next PlayMedia.
        /// </summary>
        public Task StopPlaybackAsync()
        {
            if (_mediaPlayer == null || _isClosing) return Task.CompletedTask;

            var mp = _mediaPlayer;
            var oldMedia = _currentVlcMedia;
            _currentVlcMedia = null;
            _playToken++;
            _firstFrameReady = false;
            _fadeInRequested = false;
            _overlayWindow.SetBlack();

            if (_firstFrameHandler != null)
            {
                mp.TimeChanged -= _firstFrameHandler;
                _firstFrameHandler = null;
            }

            return Task.Run(() =>
            {
                lock (_playerLock)
                {
                    try
                    {
                        mp?.Stop();
                        oldMedia?.Dispose();
                    }
                    catch { }
                }
            });
        }

        /// <summary>
        /// Fades in the video by hiding the black overlay.
        /// </summary>
        public void FadeIn(int durationMs)
        {
            if (_isClosing) return;

            _fadeInRequested = true;
            _fadeInDurationMs = durationMs;
            if (!_firstFrameReady) return;

            _fadeInRequested = false;
            _overlayWindow.FadeFromBlack(durationMs);
        }

        /// <summary>
        /// Fades out the video by showing the black overlay.
        /// </summary>
        public void FadeOut(int durationMs)
        {
            if (_isClosing) return;

            _fadeInRequested = false;
            _overlayWindow.FadeToBlack(durationMs);
        }

        /// <summary>
        /// Applies appearance settings (brightness, gradients) to the overlay.
        /// </summary>
        public void ApplyAppearance(ScreensaverSettings settings)
        {
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

            var mp = _mediaPlayer;
            var vlc = _libVLC;
            var media = _currentVlcMedia;
            _playToken++;
            if (mp != null && _firstFrameHandler != null)
                mp.TimeChanged -= _firstFrameHandler;
            _firstFrameHandler = null;
            _overlayWindow.SetBlack();
            VideoView.MediaPlayer = null;
            VideoView.Dispose();
            _mediaPlayer = null;
            _libVLC = null;
            _currentVlcMedia = null;

            Task.Run(() =>
            {
                lock (_playerLock)
                {
                    try { mp?.Stop(); mp?.Dispose(); media?.Dispose(); vlc?.Dispose(); } catch { }
                }
            });

            Close();
        }

        // User input on any screen closes all screensaver windows
        private void Window_KeyDown(object sender, KeyEventArgs e) => MultiMonitorManager.CloseAll();
        private void Window_MouseDown(object sender, MouseButtonEventArgs e) => MultiMonitorManager.CloseAll();
    }
}
