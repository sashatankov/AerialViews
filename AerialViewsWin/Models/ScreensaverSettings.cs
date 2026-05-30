using System.IO;
using Newtonsoft.Json;

namespace AerialViews.Models
{
    public class SourceSettings
    {
        public bool Enabled { get; set; } = true;
        public VideoQuality Quality { get; set; } = VideoQuality.Video1080SDR;
        public HashSet<SceneType> Scenes { get; set; } = new()
        {
            SceneType.Nature, SceneType.Countryside, SceneType.Beach,
            SceneType.City, SceneType.Sea, SceneType.Space,
            SceneType.Waterfall, SceneType.Patterns, SceneType.Fire
        };
        public HashSet<TimeOfDay> TimesOfDay { get; set; } = new()
        {
            TimeOfDay.Day, TimeOfDay.Night, TimeOfDay.Sunset, TimeOfDay.Sunrise
        };
    }

    public class OverlaySettings
    {
        // Overlay slots (8 total)
        public OverlayType SlotTopLeft1 { get; set; } = OverlayType.Empty;
        public OverlayType SlotTopLeft2 { get; set; } = OverlayType.Empty;
        public OverlayType SlotTopRight1 { get; set; } = OverlayType.Empty;
        public OverlayType SlotTopRight2 { get; set; } = OverlayType.Empty;
        public OverlayType SlotBottomLeft1 { get; set; } = OverlayType.Clock;
        public OverlayType SlotBottomLeft2 { get; set; } = OverlayType.Empty;
        public OverlayType SlotBottomRight1 { get; set; } = OverlayType.Location;
        public OverlayType SlotBottomRight2 { get; set; } = OverlayType.Empty;

        // Clock
        public ClockType ClockFormat { get; set; } = ClockType.Default;
        public int ClockSize { get; set; } = 18;
        public int ClockWeight { get; set; } = 300;

        // Date
        public DateType DateFormat { get; set; } = DateType.Compact;
        public string DateCustom { get; set; } = "yyyy-MM-dd";
        public int DateSize { get; set; } = 18;
        public int DateWeight { get; set; } = 300;

        // Location / Description
        public DescriptionManifestType DescriptionStyle { get; set; } = DescriptionManifestType.POI;
        public int DescriptionSize { get; set; } = 18;
        public int DescriptionWeight { get; set; } = 300;

        // Message
        public string MessageLine1 { get; set; } = "";
        public string MessageLine2 { get; set; } = "";
        public string MessageLine3 { get; set; } = "";
        public string MessageLine4 { get; set; } = "";
        public int MessageSize { get; set; } = 18;
        public int MessageWeight { get; set; } = 300;

        // Countdown
        public string CountdownTargetTime { get; set; } = "";
        public string CountdownTargetMessage { get; set; } = "";
        public int CountdownSize { get; set; } = 18;
        public int CountdownWeight { get; set; } = 300;

        // General overlay behaviour
        public bool AlternateTextPosition { get; set; } = true;
    }

    public class AppearanceSettings
    {
        // Fade durations (ms)
        public int OverlayFadeInDuration { get; set; } = 600;
        public int OverlayFadeOutDuration { get; set; } = 600;
        public int MediaFadeInDuration { get; set; } = 600;
        public int MediaFadeOutDuration { get; set; } = 800;

        // Auto-hide
        public int OverlayAutoHideSeconds { get; set; } = -1; // -1 = disabled
        public int OverlayRevealTimeoutSeconds { get; set; } = 4;

        // Background
        public string BackgroundLoading { get; set; } = "BLACK";
        public string BackgroundVideos { get; set; } = "BLACK";

        // Brightness
        public int VideoBrightness { get; set; } = 100;

        // Gradients
        public bool ShowTopGradient { get; set; } = false;
        public bool ShowBottomGradient { get; set; } = true;

        // Typography
        public string FontTypeface { get; set; } = "Open Sans";

        // Progress Bar
        public ProgressBarLocation ProgressBarLocation { get; set; } = ProgressBarLocation.Disabled;
        public ProgressBarType ProgressBarType { get; set; } = ProgressBarType.Both;
        public int ProgressBarOpacity { get; set; } = 100;

        // Video scaling
        public VideoScale VideoScale { get; set; } = VideoScale.ScaleToFitWithCropping;
    }

    public class PlaylistSettings
    {
        public bool RemoveDuplicates { get; set; } = true;
        public bool ShuffleVideos { get; set; } = true;
        public int SleepTimerMinutes { get; set; } = 0; // 0 = disabled

        // Video
        public bool MuteVideos { get; set; } = true;
        public int VideoVolume { get; set; } = 100;
        public double PlaybackSpeed { get; set; } = 1.0;

        // Video Advanced
        public int MaxVideoLengthSeconds { get; set; } = 0; // 0 = unlimited
        public bool LoopUntilSkipped { get; set; } = false;
        public bool LoopShortVideos { get; set; } = false;
        public LimitLongerVideos LimitLongerVideos { get; set; } = LimitLongerVideos.Limit;
        public bool RandomStartPosition { get; set; } = false;
        public int RandomStartPositionRange { get; set; } = 50;
    }

    public class ScreensaverSettings
    {
        // Video Sources
        public SourceSettings AppleSource { get; set; } = new();
        public SourceSettings AmazonSource { get; set; } = new();
        public SourceSettings Comm1Source { get; set; } = new();
        public SourceSettings Comm2Source { get; set; } = new();

        // Overlays
        public OverlaySettings Overlays { get; set; } = new();

        // Appearance
        public AppearanceSettings Appearance { get; set; } = new();

        // Playlist
        public PlaylistSettings Playlist { get; set; } = new();

        // Settings file path
        private static string SettingsFilePath
        {
            get
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var dir = Path.Combine(appData, "AerialViews");
                Directory.CreateDirectory(dir);
                return Path.Combine(dir, "settings.json");
            }
        }

        public static ScreensaverSettings Load()
        {
            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    var json = File.ReadAllText(SettingsFilePath);
                    return JsonConvert.DeserializeObject<ScreensaverSettings>(json) ?? new ScreensaverSettings();
                }
            }
            catch
            {
                // If loading fails, return defaults
            }
            return new ScreensaverSettings();
        }

        public void Save()
        {
            try
            {
                var json = JsonConvert.SerializeObject(this, Formatting.Indented);
                File.WriteAllText(SettingsFilePath, json);
            }
            catch
            {
                // Silently fail if we can't save
            }
        }
    }
}
