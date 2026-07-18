using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;
using AerialViews.Models;
using AerialViews.UI;

namespace AerialViews.Services
{
    /// <summary>
    /// Orchestrates multi-monitor screensaver playback.
    /// Creates a primary ScreensaverWindow (with overlays) on the primary display,
    /// and SecondaryScreensaverWindows (video-only) on additional displays.
    /// All screens play the same video in sync.
    /// </summary>
    public static class MultiMonitorManager
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int X, int Y, int cx, int cy, uint uFlags);

        private static readonly IntPtr HWND_TOPMOST = new(-1);
        private const uint SWP_SHOWWINDOW = 0x0040;

        private static ScreensaverWindow? _primaryWindow;
        private static readonly List<SecondaryScreensaverWindow> _secondaryWindows = new();
        private static bool _isClosing;

        /// <summary>
        /// Launches the screensaver on all connected displays.
        /// Primary display gets the full ScreensaverWindow with overlays.
        /// Secondary displays get simplified video-only windows.
        /// </summary>
        public static void LaunchScreensaver(System.Windows.Application app)
        {
            _isClosing = false;
            _secondaryWindows.Clear();

            var screens = Screen.AllScreens;
            var primaryScreen = Screen.PrimaryScreen ?? screens[0];

            // Primary window — uses XAML WindowState="Maximized" on the primary screen
            _primaryWindow = new ScreensaverWindow();

            // Create secondary windows for additional monitors
            foreach (var screen in screens)
            {
                if (screen.DeviceName == primaryScreen.DeviceName) continue;

                var sw = new SecondaryScreensaverWindow();
                sw.ScreenBounds = screen.Bounds;
                sw.WindowStartupLocation = WindowStartupLocation.Manual;
                sw.Left = screen.Bounds.Left;
                sw.Top = screen.Bounds.Top;
                sw.Width = screen.Bounds.Width;
                sw.Height = screen.Bounds.Height;
                _secondaryWindows.Add(sw);
            }

            // Show secondary windows first (their Loaded events initialize VLC)
            foreach (var sw in _secondaryWindows)
            {
                sw.Show();
            }

            // Run with primary window (blocks until shutdown)
            app.Run(_primaryWindow);
        }

        /// <summary>
        /// Positions a window precisely on a screen using Win32 pixel coordinates.
        /// Bypasses WPF DPI scaling to ensure correct placement on multi-DPI setups.
        /// </summary>
        public static void SetWindowPosition(IntPtr hwnd, System.Drawing.Rectangle bounds)
        {
            SetWindowPos(hwnd, HWND_TOPMOST, bounds.Left, bounds.Top,
                bounds.Width, bounds.Height, SWP_SHOWWINDOW);
        }

        /// <summary>
        /// Plays the specified media on all secondary screens with the same settings.
        /// </summary>
        public static void PlayOnSecondaries(AerialMedia media, ScreensaverSettings settings, int? randomStartTime)
        {
            foreach (var sw in _secondaryWindows)
            {
                sw.PlayMedia(media, settings, randomStartTime);
            }
        }

        /// <summary>
        /// Fades in all secondary screen overlays.
        /// </summary>
        public static void FadeInSecondaries(int durationMs)
        {
            foreach (var sw in _secondaryWindows)
            {
                sw.FadeIn(durationMs);
            }
        }

        /// <summary>
        /// Fades out all secondary screen overlays.
        /// </summary>
        public static void FadeOutSecondaries(int durationMs)
        {
            foreach (var sw in _secondaryWindows)
            {
                sw.FadeOut(durationMs);
            }
        }

        /// <summary>
        /// Stops video playback on all secondary screens. The primary window
        /// awaits this before playing the next video so no secondary is still
        /// tearing down its previous media.
        /// </summary>
        public static Task StopSecondariesAsync()
        {
            var windows = _secondaryWindows.ToArray();
            return windows.Length == 0
                ? Task.CompletedTask
                : Task.WhenAll(windows.Select(sw => sw.StopPlaybackAsync()));
        }

        /// <summary>
        /// Applies appearance settings (brightness, gradients) on all secondary screens.
        /// </summary>
        public static void ApplyAppearanceOnSecondaries(ScreensaverSettings settings)
        {
            foreach (var sw in _secondaryWindows)
            {
                sw.ApplyAppearance(settings);
            }
        }

        /// <summary>
        /// Closes only secondary windows. Called by the primary window during its shutdown.
        /// </summary>
        public static void CloseSecondaries()
        {
            foreach (var sw in _secondaryWindows)
            {
                try { sw.CloseScreensaver(); } catch { }
            }
            _secondaryWindows.Clear();
        }

        /// <summary>
        /// Closes all screensaver windows (secondary + primary).
        /// Called by secondary windows when user input is detected.
        /// </summary>
        public static void CloseAll()
        {
            if (_isClosing) return;
            _isClosing = true;

            CloseSecondaries();
            _primaryWindow?.CloseScreensaver();
        }
    }
}
