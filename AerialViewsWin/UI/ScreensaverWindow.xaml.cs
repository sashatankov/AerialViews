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
        private DateTime _startTime;
        private OverlayWindow? _overlayWindow;

        // Transition state machine. Mutated only on the UI thread; VLC event
        // handlers marshal via Dispatcher and validate _playToken before acting.
        private enum TransitionState { Loading, FadingIn, Playing, FadingOut, Stopping }
        private TransitionState _state = TransitionState.Loading;
        private DateTime _stateEnteredAt = DateTime.Now;
        private int _playToken;
        private Media? _currentVlcMedia;
        private readonly object _playerLock = new();
        private int _consecutiveFailures;
        private DispatcherTimer? _revealTimeoutTimer;

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
            CreateMediaPlayer();

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

            // Start progress bar timer (also drives the pre-emptive fade-out)
            _progressTimer.Interval = TimeSpan.FromMilliseconds(250);
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

        // Creates and configures the media player and binds it to the VideoView.
        // Also used by the watchdog to replace a player whose Stop() wedged.
        private void CreateMediaPlayer()
        {
            if (_libVLC == null) return;

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
            _mediaPlayer.EncounteredError += OnMediaError;
        }

        private void SetState(TransitionState state, string reason)
        {
            _state = state;
            _stateEnteredAt = DateTime.Now;
            Log($"State -> {state} ({reason})");
        }

        private void PlayMedia(AerialMedia media)
        {
            if (_libVLC == null || _mediaPlayer == null || _isClosing) return;

            var token = ++_playToken;
            SetState(TransitionState.Loading, $"play {media.Uri}");

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

                // Max video length is enforced by the pre-emptive fade in
                // OnProgressTimerTick, NOT via :stop-time. A :stop-time cutoff
                // makes VLC terminate its input right when our background
                // Stop() runs, and libvlc 3.x can deadlock when a stop races
                // end-of-input processing. Loop mode is the exception: there we
                // never Stop() mid-video, and :stop-time bounds the loop segment.
                if (_settings.Playlist.MaxVideoLengthSeconds > 0 && _settings.Playlist.LoopUntilSkipped)
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

                // Loop natively inside VLC: no EndReached, no stop/restart, no gap
                if (_settings.Playlist.LoopUntilSkipped)
                {
                    vlcMedia.AddOption(":input-repeat=65535");
                }

                _currentVlcMedia = vlcMedia;

                // Reveal on the first TimeChanged event — the earliest reliable
                // signal that frames are actually being presented. (Playing can
                // fire before any frame is rendered, especially on streams.)
                var mp = _mediaPlayer;
                void OnFirstTimeChanged(object? s, MediaPlayerTimeChangedEventArgs args)
                {
                    mp.TimeChanged -= OnFirstTimeChanged;
                    Dispatcher.BeginInvoke(() => RevealVideo(token));
                }
                mp.TimeChanged += OnFirstTimeChanged;

                StartRevealTimeout(token);

                mp.Play(vlcMedia);

                // Play the same video on all secondary screens
                MultiMonitorManager.PlayOnSecondaries(media, _settings, randomStart);
            }
            catch
            {
                // If a video fails to even start loading, skip to next
                Dispatcher.BeginInvoke(() => HandlePlaybackFailure(token));
            }
        }

        private void StartRevealTimeout(int token)
        {
            _revealTimeoutTimer?.Stop();
            _revealTimeoutTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _revealTimeoutTimer.Tick += (s, e) =>
            {
                _revealTimeoutTimer?.Stop();
                HandlePlaybackFailure(token);
            };
            _revealTimeoutTimer.Start();
        }

        private void RevealVideo(int token)
        {
            if (_isClosing || token != _playToken || _state != TransitionState.Loading) return;

            _revealTimeoutTimer?.Stop();
            _consecutiveFailures = 0;
            SetState(TransitionState.FadingIn, "first frame rendered");
            FadeIn(token);
        }

        private void FadeIn(int token)
        {
            if (_overlayWindow == null) return;

            // Fade the black overlay from opaque to transparent, revealing the video underneath
            var fadeIn = new DoubleAnimation(1, 0,
                TimeSpan.FromMilliseconds(_settings.Appearance.MediaFadeInDuration));
            fadeIn.Completed += (s, e) =>
            {
                if (_isClosing || token != _playToken) return;
                if (_state == TransitionState.FadingIn)
                {
                    SetState(TransitionState.Playing, "fade-in complete");
                }
            };
            _overlayWindow.FadeOverlay.BeginAnimation(System.Windows.UIElement.OpacityProperty, fadeIn);

            // Fade in secondary screens in sync
            MultiMonitorManager.FadeInSecondaries(_settings.Appearance.MediaFadeInDuration);
        }

        // Single entry point for advancing to the next video. Both the
        // pre-emptive progress timer and the EndReached fallback funnel in
        // here; only the first caller wins, so the playlist can't be
        // advanced twice for one video.
        private void BeginTransitionToNext()
        {
            if (_isClosing) return;
            if (_state != TransitionState.Playing && _state != TransitionState.FadingIn) return;

            SetState(TransitionState.FadingOut, "transition to next video");
            FadeOut(OnFadeOutCompleted);
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

            // Start from the overlay's current opacity so a fade-out that
            // interrupts a running fade-in doesn't snap to transparent first.
            var fadeOut = new DoubleAnimation(_overlayWindow.FadeOverlay.Opacity, 1,
                TimeSpan.FromMilliseconds(_settings.Appearance.MediaFadeOutDuration));
            fadeOut.Completed += (s, e) => onCompleted();
            _overlayWindow.FadeOverlay.BeginAnimation(System.Windows.UIElement.OpacityProperty, fadeOut);
        }

        private void OnFadeOutCompleted()
        {
            if (_isClosing) return;

            SetState(TransitionState.Stopping, "fade-out complete");
            StopPlayersThen(PlayNext);
        }

        // Stops the primary and secondary players off the UI thread, then runs
        // the continuation back on the UI thread. VLC Stop() blocks on VLC
        // threads that need the UI message pump, so it must never run on the
        // UI thread. The screen is fully black here, so the latency is invisible.
        private void StopPlayersThen(Action continuation)
        {
            var mp = _mediaPlayer;
            var oldMedia = _currentVlcMedia;
            _currentVlcMedia = null;

            var primaryStop = Task.Run(() =>
            {
                lock (_playerLock)
                {
                    try
                    {
                        Log("StopPlayersThen: stopping primary player");
                        mp?.Stop();
                        Log("StopPlayersThen: primary player stopped");
                        oldMedia?.Dispose();
                        Log("StopPlayersThen: old media disposed");
                    }
                    catch (Exception ex)
                    {
                        Log($"StopPlayersThen: stop failed: {ex.Message}");
                    }
                }
            });

            Task.WhenAll(primaryStop, MultiMonitorManager.StopSecondariesAsync())
                .ContinueWith(_ => Dispatcher.BeginInvoke(() =>
                {
                    Log("StopPlayersThen: all players stopped, continuing");
                    if (_isClosing) return;
                    // A stop that un-wedges after the watchdog already
                    // recovered must not advance the playlist again.
                    if (_state != TransitionState.Stopping) return;
                    continuation();
                }));
        }

        // Last-resort recovery: a background Stop() never returned (libvlc
        // deadlock). Abandon the wedged player (deliberately leaked — joining
        // it is what hangs) and continue playback on a fresh one.
        private void RecoverFromStuckStop()
        {
            if (_libVLC == null || _isClosing) return;

            Log("Watchdog: Stop() wedged for >8s — replacing media player");

            if (_mediaPlayer != null)
            {
                _mediaPlayer.EndReached -= OnMediaEndReached;
                _mediaPlayer.EncounteredError -= OnMediaError;
            }
            _currentVlcMedia = null;

            CreateMediaPlayer();
            PlayNext();
        }

        private void HandlePlaybackFailure(int token)
        {
            if (_isClosing || token != _playToken) return;
            // A transition is already tearing this video down — let it finish
            if (_state == TransitionState.FadingOut || _state == TransitionState.Stopping) return;

            _consecutiveFailures++;
            Log($"Playback failure #{_consecutiveFailures} (state={_state})");

            _revealTimeoutTimer?.Stop();

            // Snap to black (the failed video may be showing garbage or nothing)
            if (_overlayWindow != null)
            {
                _overlayWindow.FadeOverlay.BeginAnimation(
                    System.Windows.UIElement.OpacityProperty, null);
                _overlayWindow.FadeOverlay.Opacity = 1;
            }

            SetState(TransitionState.Stopping, "playback failure");
            StopPlayersThen(() =>
            {
                if (_consecutiveFailures >= 5)
                {
                    // Back off so a dead network doesn't hot-loop through the playlist
                    var delay = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
                    delay.Tick += (s, e) =>
                    {
                        delay.Stop();
                        if (!_isClosing) PlayNext();
                    };
                    delay.Start();
                }
                else
                {
                    PlayNext();
                }
            });
        }

        private void OnMediaEndReached(object? sender, EventArgs e)
        {
            // Fires on a VLC thread: no LibVLC calls here, just marshal to UI
            var token = _playToken;
            Dispatcher.BeginInvoke(() =>
            {
                if (_isClosing || token != _playToken) return;

                // Fallback: video ended before the pre-emptive fade could
                // trigger (unknown duration or very short video). Fades over
                // the frozen last frame. Ignored when a transition is already
                // in flight (FadingOut/Stopping) or this event is stale (Loading).
                BeginTransitionToNext();
            });
        }

        private void OnMediaError(object? sender, EventArgs e)
        {
            // Fires on a VLC thread: no LibVLC calls here, just marshal to UI
            var token = _playToken;
            Dispatcher.BeginInvoke(() => HandlePlaybackFailure(token));
        }

        private void OnOverlayTimerTick(object? sender, EventArgs e)
        {
            var current = _playlistManager.Current;
            if (current == null) return;

            // Don't read player properties while a background Stop() may be
            // running — see the deadlock note in OnProgressTimerTick.
            int position = 0;
            if (_state == TransitionState.Playing
                || _state == TransitionState.FadingIn
                || _state == TransitionState.FadingOut)
            {
                position = (int)((_mediaPlayer?.Time ?? 0) / 1000);
            }
            UpdateOverlays(current, position);
        }

        private void OnProgressTimerTick(object? sender, EventArgs e)
        {
            if (_mediaPlayer == null || _overlayWindow == null) return;

            // Watchdog: if a background Stop() wedged, never leave the user on
            // a black screen — abandon the stuck player and continue with a
            // fresh one. Must run before any player access.
            if (_state == TransitionState.Stopping && !_isClosing
                && (DateTime.Now - _stateEnteredAt).TotalSeconds > 8)
            {
                RecoverFromStuckStop();
                return;
            }

            // Never touch the player while a background Stop() may be running
            // (Stopping) or has just finished (Loading): libvlc property reads
            // take the player's internal lock, and blocking the UI thread on a
            // lock held by Stop() — which itself needs the UI message pump to
            // tear down the video window — deadlocks the whole app.
            if (_state == TransitionState.Stopping || _state == TransitionState.Loading) return;

            var progress = _mediaPlayer.Position * 100;
            _overlayWindow.VideoProgressBar.Value = progress;

            // Pre-emptive fade-out: start fading BEFORE the video ends so the
            // transition happens while the video is still playing (no freeze-frame).
            if (_state == TransitionState.Playing && !_isClosing && !_settings.Playlist.LoopUntilSkipped)
            {
                var lengthMs = _mediaPlayer.Length;   // total duration in ms (0/-1 while unknown)
                var fadeDuration = _settings.Appearance.MediaFadeOutDuration;

                // Max video length is enforced here (not via :stop-time), so
                // the effective end is whichever comes first.
                var effectiveEndMs = lengthMs;
                if (_settings.Playlist.MaxVideoLengthSeconds > 0)
                {
                    var stopTimeMs = _settings.Playlist.MaxVideoLengthSeconds * 1000L;
                    effectiveEndMs = lengthMs > 0 ? Math.Min(lengthMs, stopTimeMs) : stopTimeMs;
                }

                if (effectiveEndMs > 0)
                {
                    var remainingMs = effectiveEndMs - _mediaPlayer.Time;

                    // Margin of timer interval + slack so the fade finishes —
                    // and the background Stop() runs — a full second before
                    // the video's natural end (keeps Stop clear of libvlc's
                    // own end-of-input handling).
                    if (remainingMs <= fadeDuration + 1250)
                    {
                        BeginTransitionToNext();
                    }
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
            _revealTimeoutTimer?.Stop();

            // Close all secondary screensaver windows
            MultiMonitorManager.CloseSecondaries();

            _overlayWindow?.Close();

            // Detach VLC from the VideoView to release the HWND immediately
            var mp = _mediaPlayer;
            var vlc = _libVLC;
            var media = _currentVlcMedia;
            _mediaPlayer = null;
            _libVLC = null;
            _currentVlcMedia = null;

            // VLC Stop()/Dispose() must run off the UI thread to avoid deadlock:
            // Stop() blocks waiting for VLC threads, which need the UI message pump.
            // _playerLock serializes this with a transition's background Stop.
            Task.Run(() =>
            {
                lock (_playerLock)
                {
                    try
                    {
                        mp?.Stop();
                        mp?.Dispose();
                        media?.Dispose();
                        vlc?.Dispose();
                    }
                    catch { }
                }
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
