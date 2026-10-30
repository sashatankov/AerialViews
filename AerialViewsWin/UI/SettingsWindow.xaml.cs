using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using AerialViews.Models;
using AerialViews.Providers;
using AerialViews.Services;

namespace AerialViews.UI
{
    public partial class SettingsWindow : Window
    {
        private ScreensaverSettings _settings;
        private readonly Dictionary<AerialMediaSource, CancellationTokenSource> _downloadCts = new();

        public SettingsWindow()
        {
            InitializeComponent();
            _settings = ScreensaverSettings.Load();

            PopulateOverlayComboBoxes();
            LoadSettingsToUI();
            RefreshCacheInfo();

            // Wire up slider events for label updates
            VideoBrightness.ValueChanged += (s, e) => BrightnessLabel.Text = $"{(int)VideoBrightness.Value}%";
            VideoVolume.ValueChanged += (s, e) => VolumeLabel.Text = $"{(int)VideoVolume.Value}%";

            // Subscribe after loading so initialization never overwrites saved preferences.
            AddHandler(ToggleButton.CheckedEvent, new RoutedEventHandler(SettingsChanged));
            AddHandler(ToggleButton.UncheckedEvent, new RoutedEventHandler(SettingsChanged));
            AddHandler(Selector.SelectionChangedEvent, new SelectionChangedEventHandler((s, e) =>
            {
                if (e.OriginalSource is ComboBox) SettingsChanged(s, e);
            }));
            AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler((s, e) => SettingsChanged(s, e)));
            AddHandler(RangeBase.ValueChangedEvent, new RoutedPropertyChangedEventHandler<double>((s, e) =>
            {
                if (e.OriginalSource is Slider) SettingsChanged(s, e);
            }));
        }

        private void SettingsChanged(object sender, RoutedEventArgs e)
        {
            TrySaveSettings();
        }

        private bool TrySaveSettings()
        {
            try
            {
                SaveSettingsFromUI();
                _settings.Save();
                SaveStatus.Text = "Changes saved automatically.";
                return true;
            }
            catch (Exception ex)
            {
                SaveStatus.Text = $"Changes could not be saved: {ex.Message}";
                return false;
            }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (!TrySaveSettings())
            {
                e.Cancel = true;
                MessageBox.Show(this, SaveStatus.Text, "Unable to Save Settings",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            base.OnClosing(e);
        }

        private void PopulateOverlayComboBoxes()
        {
            var overlaySlots = new[] {
                SlotTopLeft1, SlotTopLeft2, SlotTopRight1, SlotTopRight2,
                SlotBottomLeft1, SlotBottomLeft2, SlotBottomRight1, SlotBottomRight2
            };

            var overlayNames = new Dictionary<OverlayType, string>
            {
                { OverlayType.Empty, "None" },
                { OverlayType.Clock, "Clock" },
                { OverlayType.Date, "Date" },
                { OverlayType.Location, "Location / Description" },
                { OverlayType.Message1, "Custom Message 1" },
                { OverlayType.Message2, "Custom Message 2" },
                { OverlayType.Message3, "Custom Message 3" },
                { OverlayType.Message4, "Custom Message 4" },
                { OverlayType.Countdown, "Countdown Timer" },
            };

            foreach (var combo in overlaySlots)
            {
                foreach (var pair in overlayNames)
                {
                    combo.Items.Add(new ComboBoxItem
                    {
                        Content = pair.Value,
                        Tag = pair.Key.ToString()
                    });
                }
            }
        }

        private void LoadSettingsToUI()
        {
            // Sources
            AppleEnabled.IsChecked = _settings.AppleSource.Enabled;
            SetComboByTag(AppleQuality, _settings.AppleSource.Quality.ToString());
            AppleNature.IsChecked = _settings.AppleSource.Scenes.Contains(SceneType.Nature);
            AppleBeach.IsChecked = _settings.AppleSource.Scenes.Contains(SceneType.Beach);
            AppleCity.IsChecked = _settings.AppleSource.Scenes.Contains(SceneType.City);
            AppleSea.IsChecked = _settings.AppleSource.Scenes.Contains(SceneType.Sea);
            AppleSpace.IsChecked = _settings.AppleSource.Scenes.Contains(SceneType.Space);
            AppleCountryside.IsChecked = _settings.AppleSource.Scenes.Contains(SceneType.Countryside);
            AppleDay.IsChecked = _settings.AppleSource.TimesOfDay.Contains(TimeOfDay.Day);
            AppleNight.IsChecked = _settings.AppleSource.TimesOfDay.Contains(TimeOfDay.Night);
            AppleSunset.IsChecked = _settings.AppleSource.TimesOfDay.Contains(TimeOfDay.Sunset);
            AppleSunrise.IsChecked = _settings.AppleSource.TimesOfDay.Contains(TimeOfDay.Sunrise);

            AmazonEnabled.IsChecked = _settings.AmazonSource.Enabled;
            SetComboByTag(AmazonQuality, _settings.AmazonSource.Quality.ToString());
            AmazonNature.IsChecked = _settings.AmazonSource.Scenes.Contains(SceneType.Nature);
            AmazonBeach.IsChecked = _settings.AmazonSource.Scenes.Contains(SceneType.Beach);
            AmazonSea.IsChecked = _settings.AmazonSource.Scenes.Contains(SceneType.Sea);
            AmazonPatterns.IsChecked = _settings.AmazonSource.Scenes.Contains(SceneType.Patterns);
            AmazonDay.IsChecked = _settings.AmazonSource.TimesOfDay.Contains(TimeOfDay.Day);
            AmazonNight.IsChecked = _settings.AmazonSource.TimesOfDay.Contains(TimeOfDay.Night);
            AmazonSunset.IsChecked = _settings.AmazonSource.TimesOfDay.Contains(TimeOfDay.Sunset);
            AmazonSunrise.IsChecked = _settings.AmazonSource.TimesOfDay.Contains(TimeOfDay.Sunrise);

            Comm1Enabled.IsChecked = _settings.Comm1Source.Enabled;
            SetComboByTag(Comm1Quality, _settings.Comm1Source.Quality.ToString());
            Comm2Enabled.IsChecked = _settings.Comm2Source.Enabled;
            SetComboByTag(Comm2Quality, _settings.Comm2Source.Quality.ToString());

            // Overlays
            SetComboByTag(SlotTopLeft1, _settings.Overlays.SlotTopLeft1.ToString());
            SetComboByTag(SlotTopLeft2, _settings.Overlays.SlotTopLeft2.ToString());
            SetComboByTag(SlotTopRight1, _settings.Overlays.SlotTopRight1.ToString());
            SetComboByTag(SlotTopRight2, _settings.Overlays.SlotTopRight2.ToString());
            SetComboByTag(SlotBottomLeft1, _settings.Overlays.SlotBottomLeft1.ToString());
            SetComboByTag(SlotBottomLeft2, _settings.Overlays.SlotBottomLeft2.ToString());
            SetComboByTag(SlotBottomRight1, _settings.Overlays.SlotBottomRight1.ToString());
            SetComboByTag(SlotBottomRight2, _settings.Overlays.SlotBottomRight2.ToString());

            SetComboByTag(ClockFormat, _settings.Overlays.ClockFormat.ToString());
            SetComboByTag(DateFormat, _settings.Overlays.DateFormat.ToString());
            DateCustom.Text = _settings.Overlays.DateCustom;
            SetComboByTag(DescriptionStyle, _settings.Overlays.DescriptionStyle.ToString());

            MessageLine1.Text = _settings.Overlays.MessageLine1;
            MessageLine2.Text = _settings.Overlays.MessageLine2;
            MessageLine3.Text = _settings.Overlays.MessageLine3;
            MessageLine4.Text = _settings.Overlays.MessageLine4;

            CountdownTarget.Text = _settings.Overlays.CountdownTargetTime;
            CountdownMessage.Text = _settings.Overlays.CountdownTargetMessage;
            AlternatePosition.IsChecked = _settings.Overlays.AlternateTextPosition;

            // Appearance
            MediaFadeIn.Text = _settings.Appearance.MediaFadeInDuration.ToString();
            MediaFadeOut.Text = _settings.Appearance.MediaFadeOutDuration.ToString();
            OverlayFadeIn.Text = _settings.Appearance.OverlayFadeInDuration.ToString();
            OverlayFadeOut.Text = _settings.Appearance.OverlayFadeOutDuration.ToString();
            VideoBrightness.Value = _settings.Appearance.VideoBrightness;
            BrightnessLabel.Text = $"{_settings.Appearance.VideoBrightness}%";
            ShowTopGradient.IsChecked = _settings.Appearance.ShowTopGradient;
            ShowBottomGradient.IsChecked = _settings.Appearance.ShowBottomGradient;
            SetComboByTag(ProgressBarPos, _settings.Appearance.ProgressBarLocation.ToString());
            SetComboByTag(VideoScaleMode, _settings.Appearance.VideoScale.ToString());

            // Playlist
            ShuffleVideos.IsChecked = _settings.Playlist.ShuffleVideos;
            RemoveDuplicates.IsChecked = _settings.Playlist.RemoveDuplicates;
            SleepTimer.Text = _settings.Playlist.SleepTimerMinutes.ToString();
            MuteVideos.IsChecked = _settings.Playlist.MuteVideos;
            VideoVolume.Value = _settings.Playlist.VideoVolume;
            VolumeLabel.Text = $"{_settings.Playlist.VideoVolume}%";
            SetComboByTag(PlaybackSpeed, _settings.Playlist.PlaybackSpeed.ToString());
            MaxVideoLength.Text = _settings.Playlist.MaxVideoLengthSeconds.ToString();
            LoopUntilSkipped.IsChecked = _settings.Playlist.LoopUntilSkipped;
            LoopShortVideos.IsChecked = _settings.Playlist.LoopShortVideos;
            RandomStartPos.IsChecked = _settings.Playlist.RandomStartPosition;
        }

        private void SaveSettingsFromUI()
        {
            // Sources
            _settings.AppleSource.Enabled = AppleEnabled.IsChecked ?? true;
            _settings.AppleSource.Quality = ParseEnum<VideoQuality>(GetComboTag(AppleQuality));
            _settings.AppleSource.Scenes = GetCheckedScenes("Apple", _settings.AppleSource.Scenes);
            _settings.AppleSource.TimesOfDay = GetCheckedTimes("Apple");

            _settings.AmazonSource.Enabled = AmazonEnabled.IsChecked ?? true;
            _settings.AmazonSource.Quality = ParseEnum<VideoQuality>(GetComboTag(AmazonQuality));
            _settings.AmazonSource.Scenes = GetCheckedScenes("Amazon", _settings.AmazonSource.Scenes);
            _settings.AmazonSource.TimesOfDay = GetCheckedTimes("Amazon");

            _settings.Comm1Source.Enabled = Comm1Enabled.IsChecked ?? true;
            _settings.Comm1Source.Quality = ParseEnum<VideoQuality>(GetComboTag(Comm1Quality));

            _settings.Comm2Source.Enabled = Comm2Enabled.IsChecked ?? true;
            _settings.Comm2Source.Quality = ParseEnum<VideoQuality>(GetComboTag(Comm2Quality));

            // Overlays
            _settings.Overlays.SlotTopLeft1 = ParseEnum<OverlayType>(GetComboTag(SlotTopLeft1));
            _settings.Overlays.SlotTopLeft2 = ParseEnum<OverlayType>(GetComboTag(SlotTopLeft2));
            _settings.Overlays.SlotTopRight1 = ParseEnum<OverlayType>(GetComboTag(SlotTopRight1));
            _settings.Overlays.SlotTopRight2 = ParseEnum<OverlayType>(GetComboTag(SlotTopRight2));
            _settings.Overlays.SlotBottomLeft1 = ParseEnum<OverlayType>(GetComboTag(SlotBottomLeft1));
            _settings.Overlays.SlotBottomLeft2 = ParseEnum<OverlayType>(GetComboTag(SlotBottomLeft2));
            _settings.Overlays.SlotBottomRight1 = ParseEnum<OverlayType>(GetComboTag(SlotBottomRight1));
            _settings.Overlays.SlotBottomRight2 = ParseEnum<OverlayType>(GetComboTag(SlotBottomRight2));

            _settings.Overlays.ClockFormat = ParseEnum<ClockType>(GetComboTag(ClockFormat));
            _settings.Overlays.DateFormat = ParseEnum<DateType>(GetComboTag(DateFormat));
            _settings.Overlays.DateCustom = DateCustom.Text;
            _settings.Overlays.DescriptionStyle = ParseEnum<DescriptionManifestType>(GetComboTag(DescriptionStyle));

            _settings.Overlays.MessageLine1 = MessageLine1.Text;
            _settings.Overlays.MessageLine2 = MessageLine2.Text;
            _settings.Overlays.MessageLine3 = MessageLine3.Text;
            _settings.Overlays.MessageLine4 = MessageLine4.Text;

            _settings.Overlays.CountdownTargetTime = CountdownTarget.Text;
            _settings.Overlays.CountdownTargetMessage = CountdownMessage.Text;
            _settings.Overlays.AlternateTextPosition = AlternatePosition.IsChecked ?? true;

            // Appearance
            int.TryParse(MediaFadeIn.Text, out int mediaFadeIn);
            _settings.Appearance.MediaFadeInDuration = mediaFadeIn;
            int.TryParse(MediaFadeOut.Text, out int mediaFadeOut);
            _settings.Appearance.MediaFadeOutDuration = mediaFadeOut;
            int.TryParse(OverlayFadeIn.Text, out int overlayFadeIn);
            _settings.Appearance.OverlayFadeInDuration = overlayFadeIn;
            int.TryParse(OverlayFadeOut.Text, out int overlayFadeOut);
            _settings.Appearance.OverlayFadeOutDuration = overlayFadeOut;
            _settings.Appearance.VideoBrightness = (int)VideoBrightness.Value;
            _settings.Appearance.ShowTopGradient = ShowTopGradient.IsChecked ?? false;
            _settings.Appearance.ShowBottomGradient = ShowBottomGradient.IsChecked ?? true;
            _settings.Appearance.ProgressBarLocation = ParseEnum<ProgressBarLocation>(GetComboTag(ProgressBarPos));
            _settings.Appearance.VideoScale = ParseEnum<VideoScale>(GetComboTag(VideoScaleMode));

            // Playlist
            _settings.Playlist.ShuffleVideos = ShuffleVideos.IsChecked ?? true;
            _settings.Playlist.RemoveDuplicates = RemoveDuplicates.IsChecked ?? true;
            int.TryParse(SleepTimer.Text, out int sleepTimer);
            _settings.Playlist.SleepTimerMinutes = sleepTimer;
            _settings.Playlist.MuteVideos = MuteVideos.IsChecked ?? true;
            _settings.Playlist.VideoVolume = (int)VideoVolume.Value;
            double.TryParse(GetComboTag(PlaybackSpeed), out double speed);
            _settings.Playlist.PlaybackSpeed = speed > 0 ? speed : 1.0;
            int.TryParse(MaxVideoLength.Text, out int maxLen);
            _settings.Playlist.MaxVideoLengthSeconds = maxLen;
            _settings.Playlist.LoopUntilSkipped = LoopUntilSkipped.IsChecked ?? false;
            _settings.Playlist.LoopShortVideos = LoopShortVideos.IsChecked ?? false;
            _settings.Playlist.RandomStartPosition = RandomStartPos.IsChecked ?? false;
        }

        private HashSet<SceneType> GetCheckedScenes(string prefix, HashSet<SceneType> existingScenes)
        {
            // Keep filters that have no control in this source's settings panel.
            var scenes = new HashSet<SceneType>(existingScenes);
            foreach (var scene in Enum.GetValues<SceneType>())
            {
                if (FindName($"{prefix}{scene}") is CheckBox checkBox)
                {
                    if (checkBox.IsChecked == true) scenes.Add(scene);
                    else scenes.Remove(scene);
                }
            }

            return scenes;
        }

        private HashSet<TimeOfDay> GetCheckedTimes(string prefix)
        {
            var times = new HashSet<TimeOfDay>();
            CheckBox? FindCheckBox(string name)
            {
                return FindName($"{prefix}{name}") as CheckBox;
            }

            if (FindCheckBox("Day")?.IsChecked ?? false) times.Add(TimeOfDay.Day);
            if (FindCheckBox("Night")?.IsChecked ?? false) times.Add(TimeOfDay.Night);
            if (FindCheckBox("Sunset")?.IsChecked ?? false) times.Add(TimeOfDay.Sunset);
            if (FindCheckBox("Sunrise")?.IsChecked ?? false) times.Add(TimeOfDay.Sunrise);

            return times;
        }

        private static void SetComboByTag(ComboBox combo, string tag)
        {
            foreach (ComboBoxItem item in combo.Items)
            {
                if (item.Tag?.ToString() == tag)
                {
                    combo.SelectedItem = item;
                    return;
                }
            }
            // Default to first item if not found
            if (combo.Items.Count > 0) combo.SelectedIndex = 0;
        }

        private static string GetComboTag(ComboBox combo)
        {
            return (combo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "";
        }

        private static T ParseEnum<T>(string value) where T : struct
        {
            if (Enum.TryParse<T>(value, out var result)) return result;
            return default;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Preview_Click(object sender, RoutedEventArgs e)
        {
            if (!TrySaveSettings()) return;

            // Open screensaver preview
            var screensaver = new ScreensaverWindow();
            screensaver.Show();
        }

        // ── Cache Management ──────────────────────────────────────────────

        private void RefreshCacheInfo()
        {
            TotalCacheSize.Text = VideoCacheManager.FormatSize(VideoCacheManager.GetCacheSize());

            UpdateSourceCacheLabel(AerialMediaSource.Apple, AppleCacheInfo);
            UpdateSourceCacheLabel(AerialMediaSource.Amazon, AmazonCacheInfo);
            UpdateSourceCacheLabel(AerialMediaSource.Comm1, Comm1CacheInfo);
            UpdateSourceCacheLabel(AerialMediaSource.Comm2, Comm2CacheInfo);
        }

        private static void UpdateSourceCacheLabel(AerialMediaSource source, TextBlock label)
        {
            var count = VideoCacheManager.GetSourceCacheCount(source);
            var size = VideoCacheManager.GetSourceCacheSize(source);
            label.Text = $"{count} file{(count == 1 ? "" : "s")} — {VideoCacheManager.FormatSize(size)}";
        }

        private void OpenCacheFolder_Click(object sender, RoutedEventArgs e)
        {
            VideoCacheManager.OpenCacheFolder();
        }

        private void ClearAllCache_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "Delete all cached videos? This cannot be undone.",
                "Clear Cache", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
            {
                VideoCacheManager.ClearCache();
                RefreshCacheInfo();
            }
        }

        // ── Per-source download handlers ──────────────────────────────────

        private void DownloadApple_Click(object sender, RoutedEventArgs e) =>
            StartDownload(AerialMediaSource.Apple, _settings.AppleSource,
                "tvos15.json", "tvos15_strings.json",
                DownloadAppleBtn, CancelAppleBtn, AppleProgress, AppleDownloadStatus, AppleCacheInfo);

        private void DownloadAmazon_Click(object sender, RoutedEventArgs e) =>
            StartDownload(AerialMediaSource.Amazon, _settings.AmazonSource,
                "fireos8.json", "fireos8_strings.json",
                DownloadAmazonBtn, CancelAmazonBtn, AmazonProgress, AmazonDownloadStatus, AmazonCacheInfo);

        private void DownloadComm1_Click(object sender, RoutedEventArgs e) =>
            StartDownload(AerialMediaSource.Comm1, _settings.Comm1Source,
                "comm1.json", "comm1_strings.json",
                DownloadComm1Btn, CancelComm1Btn, Comm1Progress, Comm1DownloadStatus, Comm1CacheInfo);

        private void DownloadComm2_Click(object sender, RoutedEventArgs e) =>
            StartDownload(AerialMediaSource.Comm2, _settings.Comm2Source,
                "comm2.json", "comm2_strings.json",
                DownloadComm2Btn, CancelComm2Btn, Comm2Progress, Comm2DownloadStatus, Comm2CacheInfo);

        private async void StartDownload(
            AerialMediaSource source, SourceSettings sourceSettings,
            string manifest, string strings,
            Button downloadBtn, Button cancelBtn,
            ProgressBar progressBar, TextBlock statusText, TextBlock cacheInfo)
        {
            // Save current settings so the download uses latest quality/filters
            SaveSettingsFromUI();

            // Build video list for this source
            var provider = new VideoProvider(manifest, strings, source);
            var (media, _) = provider.LoadMedia(sourceSettings);

            if (media.Count == 0)
            {
                MessageBox.Show("No videos match the current filter settings for this source.",
                    "Nothing to Download", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Set up cancellation
            var cts = new CancellationTokenSource();
            _downloadCts[source] = cts;

            // Update UI state
            downloadBtn.Visibility = Visibility.Collapsed;
            cancelBtn.Visibility = Visibility.Visible;
            progressBar.Visibility = Visibility.Visible;
            progressBar.Value = 0;
            statusText.Visibility = Visibility.Visible;
            statusText.Text = "Starting download...";

            var progress = new Progress<(int current, int total, string name, double fileProgress)>(p =>
            {
                var overallPercent = ((p.current - 1.0 + p.fileProgress) / p.total) * 100;
                progressBar.Value = overallPercent;
                statusText.Text = $"Downloading {p.current}/{p.total}: {p.name}";
            });

            try
            {
                var (succeeded, failed) = await VideoCacheManager.DownloadAllAsync(media, progress, cts.Token);
                statusText.Text = $"Done — {succeeded} downloaded, {failed} failed";
            }
            catch (OperationCanceledException)
            {
                statusText.Text = "Download cancelled.";
            }
            catch (Exception ex)
            {
                statusText.Text = $"Error: {ex.Message}";
            }
            finally
            {
                downloadBtn.Visibility = Visibility.Visible;
                cancelBtn.Visibility = Visibility.Collapsed;
                progressBar.Visibility = Visibility.Collapsed;
                _downloadCts.Remove(source);
                RefreshCacheInfo();
            }
        }

        // ── Cancel handlers ──

        private void CancelApple_Click(object sender, RoutedEventArgs e) => CancelDownload(AerialMediaSource.Apple);
        private void CancelAmazon_Click(object sender, RoutedEventArgs e) => CancelDownload(AerialMediaSource.Amazon);
        private void CancelComm1_Click(object sender, RoutedEventArgs e) => CancelDownload(AerialMediaSource.Comm1);
        private void CancelComm2_Click(object sender, RoutedEventArgs e) => CancelDownload(AerialMediaSource.Comm2);

        private void CancelDownload(AerialMediaSource source)
        {
            if (_downloadCts.TryGetValue(source, out var cts))
            {
                cts.Cancel();
            }
        }

        // ── Clear handlers ──

        private void ClearApple_Click(object sender, RoutedEventArgs e) => ClearSourceCache(AerialMediaSource.Apple);
        private void ClearAmazon_Click(object sender, RoutedEventArgs e) => ClearSourceCache(AerialMediaSource.Amazon);
        private void ClearComm1_Click(object sender, RoutedEventArgs e) => ClearSourceCache(AerialMediaSource.Comm1);
        private void ClearComm2_Click(object sender, RoutedEventArgs e) => ClearSourceCache(AerialMediaSource.Comm2);

        private void ClearSourceCache(AerialMediaSource source)
        {
            var result = MessageBox.Show(
                $"Delete all cached {source} videos?",
                "Clear Cache", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
            {
                VideoCacheManager.ClearCache(source);
                RefreshCacheInfo();
            }
        }
    }
}
