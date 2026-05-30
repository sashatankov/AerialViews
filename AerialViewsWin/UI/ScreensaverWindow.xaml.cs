using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using AerialViews.Models;
using AerialViews.Providers;
using AerialViews.Services;
using LibVLCSharp.Shared;

namespace AerialViews.UI
{
    public partial class ScreensaverWindow : Window
    {
        // P/Invoke for reliable cursor position reading (bypasses native HWND issues)
        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X, Y; }

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        private LibVLC? _libVLC;
        private LibVLCSharp.Shared.MediaPlayer? _mediaPlayer;
        private PlaylistManager _playlistManager = new();
        private ScreensaverSettings _settings = new();
        private DispatcherTimer _overlayTimer = new();
        private DispatcherTimer _progressTimer = new();
        private DispatcherTimer _mouseCheckTimer = new();
        private DispatcherTimer? _sleepTimer;
        private Point? _initialMousePosition;
        private POINT? _initialCursorPos;
        private bool _isClosing = false;
        private bool _isFadingToNext = false;
        private DateTime _startTime;
        private OverlayWindow? _overlayWindow;

        public LibVLCSharp.Shared.MediaPlayer? MediaPlayer => _mediaPlayer;

        private static readonly string LogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AerialViews", "debug.log");

        private static void Log(string message)
        {
            try
            {
                File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss.fff}] {message}\n");
            }
            catch { }
        }

        public ScreensaverWindow()
        {
            InitializeComponent();
            DataContext = this;
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _settings = ScreensaverSettings.Load();
            _startTime = DateTime.Now;

            // Initialize LibVLC
            Core.Initialize();
            _libVLC = new LibVLC();
            _mediaPlayer = new LibVLCSharp.Shared.MediaPlayer(_libVLC);

            // Configure audio
            if (_settings.Playlist.MuteVideos)
            {
                _mediaPlayer.Mute = true;
            }
            else
            {
                _mediaPlayer.Volume = _settings.Playlist.VideoVolume;
            }

            // Set playback speed
            _mediaPlayer.SetRate((float)_settings.Playlist.PlaybackSpeed);

            VideoView.MediaPlayer = _mediaPlayer;

            // Wire up events
            _mediaPlayer.EndReached += OnMediaEndReached;

            // Create and show the overlay window
            _overlayWindow = new OverlayWindow();
            _overlayWindow.Left = Left;
            _overlayWindow.Top = Top;
            _overlayWindow.Width = ActualWidth;
            _overlayWindow.Height = ActualHeight;
            _overlayWindow.WindowState = WindowState.Maximized;
            _overlayWindow.Show();

            // Apply appearance settings
            ApplyAppearanceSettings();

            // Apply appearance to secondary screens
            MultiMonitorManager.ApplyAppearanceOnSecondaries(_settings);

            // Build and start playlist
            _playlistManager.BuildPlaylist(_settings);

            if (_playlistManager.Count > 0)
            {
                LoadingText.Visibility = Visibility.Collapsed;
                PlayNext();
            }
            else
            {
                LoadingText.Text = "No videos available. Please check your settings.";
            }

            // Start overlay update timer
            _overlayTimer.Interval = TimeSpan.FromSeconds(1);
            _overlayTimer.Tick += OnOverlayTimerTick;
            _overlayTimer.Start();

            // Start progress bar timer
            _progressTimer.Interval = TimeSpan.FromMilliseconds(500);
            _progressTimer.Tick += OnProgressTimerTick;
            _progressTimer.Start();

            // Start mouse cursor polling timer after a delay.
            // Secondary window creation (SetWindowPos) can cause transient cursor
            // position changes; wait for everything to settle before tracking.
            _mouseCheckTimer.Interval = TimeSpan.FromMilliseconds(200);
            _mouseCheckTimer.Tick += OnMouseCheckTick;
            var mouseStartDelay = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            mouseStartDelay.Tick += (s, args) =>
            {
                mouseStartDelay.Stop();
                // Capture current cursor position as the baseline AFTER windows settled
                _initialCursorPos = null;
                _initialMousePosition = null;
                _mouseCheckTimer.Start();
            };
            mouseStartDelay.Start();

            // Setup sleep timer
            if (_settings.Playlist.SleepTimerMinutes > 0)
            {
                _sleepTimer = new DispatcherTimer();
                _sleepTimer.Interval = TimeSpan.FromMinutes(_settings.Playlist.SleepTimerMinutes);
                _sleepTimer.Tick += (s, e) => CloseScreensaver("SleepTimer");
                _sleepTimer.Start();
            }
        }

        private void ApplyAppearanceSettings()
        {
            if (_overlayWindow == null) return;

            // Brightness
            if (_settings.Appearance.VideoBrightness < 100)
            {
                _overlayWindow.BrightnessOverlay.Opacity = 1.0 - (_settings.Appearance.VideoBrightness / 100.0);
            }

            // Gradients
            _overlayWindow.TopGradient.Visibility = _settings.Appearance.ShowTopGradient ? Visibility.Visible : Visibility.Collapsed;
            _overlayWindow.BottomGradient.Visibility = _settings.Appearance.ShowBottomGradient ? Visibility.Visible : Visibility.Collapsed;

            // Progress bar
            if (_settings.Appearance.ProgressBarLocation != ProgressBarLocation.Disabled)
            {
                _overlayWindow.VideoProgressBar.Visibility = Visibility.Visible;
                _overlayWindow.VideoProgressBar.Opacity = _settings.Appearance.ProgressBarOpacity / 100.0;
                _overlayWindow.VideoProgressBar.VerticalAlignment = _settings.Appearance.ProgressBarLocation == ProgressBarLocation.Top
                    ? VerticalAlignment.Top : VerticalAlignment.Bottom;
            }
        }

        private void PlayNext()
        {
            var media = _playlistManager.GetNext();
            if (media == null) return;

            PlayMedia(media);
            UpdateOverlays(media, 0);
        }

        private void PlayMedia(AerialMedia media)
        {
            if (_libVLC == null || _mediaPlayer == null) return;

            // Reset fade state for the new video
            _isFadingToNext = false;

            // Ensure the fade overlay is fully opaque (black screen) while loading
            if (_overlayWindow != null)
            {
                _overlayWindow.FadeOverlay.BeginAnimation(
                    System.Windows.UIElement.OpacityProperty, null);
                _overlayWindow.FadeOverlay.Opacity = 1;
            }

            try
            {
                // Check cache first — play from local file if available, otherwise stream
                var resolvedUri = VideoCacheManager.ResolvePlaybackUri(media.Uri, media.Source);
                var vlcMedia = resolvedUri.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    ? new Media(_libVLC, new Uri(resolvedUri))
                    : new Media(_libVLC, resolvedUri);

                // Apply max video length
                if (_settings.Playlist.MaxVideoLengthSeconds > 0)
                {
                    vlcMedia.AddOption($":stop-time={_settings.Playlist.MaxVideoLengthSeconds}");
                }

                // Apply random start position (generate once, share with secondary screens)
                int? randomStart = null;
                if (_settings.Playlist.RandomStartPosition)
                {
                    randomStart = new Random().Next(0, _settings.Playlist.RandomStartPositionRange);
                    vlcMedia.AddOption($":start-time={randomStart}");
                }

                // Wait for VLC to actually start rendering before fading in.
                // This prevents flashes of the previous frame or blank content.
                void OnPlaying(object? s, EventArgs args)
                {
                    _mediaPlayer.Playing -= OnPlaying;

                    // Small delay so VLC decodes and presents the first frame
                    // before we start revealing the video.
                    Task.Delay(150).ContinueWith(_ =>
                    {
                        Dispatcher.BeginInvoke(() => FadeIn());
                    });
                }

                _mediaPlayer.Playing += OnPlaying;
                _mediaPlayer.Play(vlcMedia);

                // Play the same video on all secondary screens
                MultiMonitorManager.PlayOnSecondaries(media, _settings, randomStart);
            }
            catch
            {
                // If a video fails, skip to next
                Dispatcher.BeginInvoke(() => PlayNext());
            }
        }

        private void FadeIn()
        {
            if (_overlayWindow == null) return;

            // Fade the black overlay from opaque to transparent, revealing the video underneath
            var fadeIn = new DoubleAnimation(1, 0,
                TimeSpan.FromMilliseconds(_settings.Appearance.MediaFadeInDuration));
            _overlayWindow.FadeOverlay.BeginAnimation(System.Windows.UIElement.OpacityProperty, fadeIn);

            // Fade in secondary screens in sync
            MultiMonitorManager.FadeInSecondaries(_settings.Appearance.MediaFadeInDuration);
        }

        private void FadeOut(Action onCompleted)
        {
            if (_overlayWindow == null)
            {
                onCompleted();
                return;
            }

            // Fade out secondary screens in sync
            MultiMonitorManager.FadeOutSecondaries(_settings.Appearance.MediaFadeOutDuration);

            // Fade the black overlay from transparent to opaque, hiding the video
            var fadeOut = new DoubleAnimation(0, 1,
                TimeSpan.FromMilliseconds(_settings.Appearance.MediaFadeOutDuration));
            fadeOut.Completed += (s, e) => onCompleted();
            _overlayWindow.FadeOverlay.BeginAnimation(System.Windows.UIElement.OpacityProperty, fadeOut);
        }

        private void OnMediaEndReached(object? sender, EventArgs e)
        {
            if (_isClosing) return;

            // Check if should loop
            if (_settings.Playlist.LoopUntilSkipped)
            {
                Dispatcher.BeginInvoke(() =>
                {
                    _mediaPlayer?.Stop();
                    MultiMonitorManager.StopSecondaries();
                    var current = _playlistManager.Current;
                    if (current != null) PlayMedia(current);
                });
                return;
            }

            Dispatcher.BeginInvoke(() =>
            {
                if (_isFadingToNext)
                {
                    // Pre-emptive fade is already in progress — it will handle the transition
                    return;
                }

                // Fallback: video ended before pre-emptive fade could trigger
                // (e.g., very short video or unknown duration)
                FadeOut(() => PlayNext());
            });
        }

        private void OnOverlayTimerTick(object? sender, EventArgs e)
        {
            var current = _playlistManager.Current;
            if (current == null) return;

            int position = (int)(_mediaPlayer?.Position * (_mediaPlayer?.Length / 1000.0) ?? 0);
            UpdateOverlays(current, position);
        }

        private void OnProgressTimerTick(object? sender, EventArgs e)
        {
            if (_mediaPlayer == null || _overlayWindow == null) return;

            var progress = _mediaPlayer.Position * 100;
            _overlayWindow.VideoProgressBar.Value = progress;

            // Pre-emptive fade-out: start fading BEFORE the video ends so the
            // transition happens while the video is still playing (no freeze-frame).
            if (!_isFadingToNext && !_isClosing && !_settings.Playlist.LoopUntilSkipped)
            {
                var lengthMs = _mediaPlayer.Length;   // total duration in ms
                var positionMs = _mediaPlayer.Position * lengthMs;  // current position in ms
                var remainingMs = lengthMs - positionMs;
                var fadeDuration = _settings.Appearance.MediaFadeOutDuration;

                // Trigger when remaining time is within the fade-out duration
                // (and we have a valid duration to work with)
                if (lengthMs > 0 && remainingMs <= fadeDuration && remainingMs >= 0)
                {
                    _isFadingToNext = true;
                    FadeOut(() =>
                    {
                        // Stop current video and move to next
                        _mediaPlayer?.Stop();
                        PlayNext();
                    });
                }
            }
        }

        private void UpdateOverlays(AerialMedia media, int positionSeconds)
        {
            if (_overlayWindow == null) return;

            UpdateOverlaySlot(_overlayWindow.OverlayTopLeft1, _settings.Overlays.SlotTopLeft1, media, positionSeconds);
            UpdateOverlaySlot(_overlayWindow.OverlayTopLeft2, _settings.Overlays.SlotTopLeft2, media, positionSeconds);
            UpdateOverlaySlot(_overlayWindow.OverlayTopRight1, _settings.Overlays.SlotTopRight1, media, positionSeconds);
            UpdateOverlaySlot(_overlayWindow.OverlayTopRight2, _settings.Overlays.SlotTopRight2, media, positionSeconds);
            UpdateOverlaySlot(_overlayWindow.OverlayBottomLeft1, _settings.Overlays.SlotBottomLeft1, media, positionSeconds);
            UpdateOverlaySlot(_overlayWindow.OverlayBottomLeft2, _settings.Overlays.SlotBottomLeft2, media, positionSeconds);
            UpdateOverlaySlot(_overlayWindow.OverlayBottomRight1, _settings.Overlays.SlotBottomRight1, media, positionSeconds);
            UpdateOverlaySlot(_overlayWindow.OverlayBottomRight2, _settings.Overlays.SlotBottomRight2, media, positionSeconds);
        }

        private void UpdateOverlaySlot(System.Windows.Controls.TextBlock textBlock, OverlayType type,
            AerialMedia media, int positionSeconds)
        {
            string content = GetOverlayContent(type, media, positionSeconds);
            textBlock.Text = content;
            textBlock.Visibility = string.IsNullOrEmpty(content) ? Visibility.Collapsed : Visibility.Visible;
        }

        private string GetOverlayContent(OverlayType type, AerialMedia media, int positionSeconds)
        {
            return type switch
            {
                OverlayType.Clock => GetClockText(),
                OverlayType.Date => GetDateText(),
                OverlayType.Location => _playlistManager.GetDescription(media, positionSeconds) ?? "",
                OverlayType.Message1 => _settings.Overlays.MessageLine1,
                OverlayType.Message2 => _settings.Overlays.MessageLine2,
                OverlayType.Message3 => _settings.Overlays.MessageLine3,
                OverlayType.Message4 => _settings.Overlays.MessageLine4,
                OverlayType.Countdown => GetCountdownText(),
                _ => ""
            };
        }

        private string GetClockText()
        {
            return _settings.Overlays.ClockFormat switch
            {
                ClockType.Hours12 => DateTime.Now.ToString("h:mm tt"),
                ClockType.Hours24 => DateTime.Now.ToString("HH:mm"),
                _ => DateTime.Now.ToShortTimeString()
            };
        }

        private string GetDateText()
        {
            return _settings.Overlays.DateFormat switch
            {
                DateType.Compact => DateTime.Now.ToString("ddd, MMM d"),
                DateType.Long => DateTime.Now.ToLongDateString(),
                DateType.Custom => DateTime.Now.ToString(_settings.Overlays.DateCustom),
                _ => DateTime.Now.ToString("ddd, MMM d")
            };
        }

        private string GetCountdownText()
        {
            if (string.IsNullOrEmpty(_settings.Overlays.CountdownTargetTime)) return "";

            if (DateTime.TryParse(_settings.Overlays.CountdownTargetTime, out var target))
            {
                var diff = target - DateTime.Now;
                if (diff.TotalSeconds <= 0)
                {
                    return _settings.Overlays.CountdownTargetMessage;
                }

                if (diff.Days > 0)
                    return $"{diff.Days}d {diff.Hours}h {diff.Minutes}m";
                if (diff.Hours > 0)
                    return $"{diff.Hours}h {diff.Minutes}m {diff.Seconds}s";
                return $"{diff.Minutes}m {diff.Seconds}s";
            }
            return "";
        }

        public void CloseScreensaver(string reason = "unknown")
        {
            if (_isClosing) return;
            _isClosing = true;

            Log($"CloseScreensaver reason={reason}");

            _overlayTimer.Stop();
            _progressTimer.Stop();
            _mouseCheckTimer.Stop();
            _sleepTimer?.Stop();

            // Close all secondary screensaver windows
            MultiMonitorManager.CloseSecondaries();

            _overlayWindow?.Close();

            // Detach VLC from the VideoView to release the HWND immediately
            var mp = _mediaPlayer;
            var vlc = _libVLC;
            _mediaPlayer = null;
            _libVLC = null;

            // VLC Stop()/Dispose() must run off the UI thread to avoid deadlock:
            // Stop() blocks waiting for VLC threads, which need the UI message pump.
            Task.Run(() =>
            {
                try
                {
                    mp?.Stop();
                    mp?.Dispose();
                    vlc?.Dispose();
                }
                catch { }
            });

            // Shut down the app immediately — don't wait for VLC cleanup
            Application.Current.Shutdown();
        }

        // Exit on keyboard press
        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            CloseScreensaver($"KeyDown: {e.Key}");
        }

        // Exit on mouse click
        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            CloseScreensaver("MouseDown");
        }

        // Exit on mouse move (with threshold to avoid accidental triggers)
        private void Window_MouseMove(object sender, MouseEventArgs e)
        {
            var currentPos = e.GetPosition(this);

            if (_initialMousePosition == null)
            {
                _initialMousePosition = currentPos;
                return;
            }

            var dx = Math.Abs(currentPos.X - _initialMousePosition.Value.X);
            var dy = Math.Abs(currentPos.Y - _initialMousePosition.Value.Y);

            if (dx > 10 || dy > 10)
            {
                CloseScreensaver($"MouseMove dx={dx:F1} dy={dy:F1}");
            }
        }

        // Polls the global cursor position via Win32 — works even when VLC's
        // native HWND captures the mouse, making WPF events unreliable.
        private void OnMouseCheckTick(object? sender, EventArgs e)
        {
            if (_isClosing) return;
            if (!GetCursorPos(out var pos)) return;

            if (_initialCursorPos == null)
            {
                _initialCursorPos = pos;
                return;
            }

            var dx = Math.Abs(pos.X - _initialCursorPos.Value.X);
            var dy = Math.Abs(pos.Y - _initialCursorPos.Value.Y);

            if (dx > 5 || dy > 5)
            {
                CloseScreensaver($"CursorPoll dx={dx} dy={dy} from=({_initialCursorPos.Value.X},{_initialCursorPos.Value.Y}) to=({pos.X},{pos.Y})");
            }
        }
    }
}
