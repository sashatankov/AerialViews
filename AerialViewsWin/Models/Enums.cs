namespace AerialViews.Models
{
    public enum VideoQuality
    {
        Video1080H264,
        Video1080SDR,
        Video1080HDR,
        Video4KSDR,
        Video4KHDR
    }

    public enum SceneType
    {
        Unknown,
        Nature,
        Countryside,
        Waterfall,
        Beach,
        City,
        Sea,
        Space,
        Patterns,
        Fire
    }

    public enum TimeOfDay
    {
        Unknown,
        Day,
        Night,
        Sunset,
        Sunrise
    }

    public enum OverlayType
    {
        Empty,
        Clock,
        Location,
        Date,
        Message1,
        Message2,
        Message3,
        Message4,
        Countdown
    }

    public enum ClockType
    {
        Default,
        Hours12,
        Hours24
    }

    public enum DateType
    {
        Compact,
        Long,
        Custom
    }

    public enum DescriptionManifestType
    {
        POI,
        Title
    }

    public enum DescriptionFilenameType
    {
        Disabled,
        Filename,
        FilenameAndFolder
    }

    public enum VideoScale
    {
        ScaleToFit,
        ScaleToFitWithCropping,
        Stretch
    }

    public enum ProgressBarLocation
    {
        Disabled,
        Top,
        Bottom
    }

    public enum ProgressBarType
    {
        Bar,
        Timer,
        Both
    }

    public enum LimitLongerVideos
    {
        Limit,
        Loop,
        Disabled
    }

    public enum AerialMediaSource
    {
        Apple,
        Amazon,
        Comm1,
        Comm2,
        Local
    }

    public enum AerialMediaType
    {
        Video,
        Photo
    }

    public static class EnumHelpers
    {
        public static SceneType ParseSceneType(string? value)
        {
            return value?.ToLowerInvariant() switch
            {
                "nature" => SceneType.Nature,
                "beach" => SceneType.Beach,
                "countryside" => SceneType.Countryside,
                "waterfall" => SceneType.Waterfall,
                "city" => SceneType.City,
                "sea" => SceneType.Sea,
                "space" => SceneType.Space,
                "patterns" => SceneType.Patterns,
                "fire" => SceneType.Fire,
                _ => SceneType.Unknown
            };
        }

        public static TimeOfDay ParseTimeOfDay(string? value)
        {
            return value?.ToLowerInvariant() switch
            {
                "day" => TimeOfDay.Day,
                "night" => TimeOfDay.Night,
                "sunset" => TimeOfDay.Sunset,
                "sunrise" => TimeOfDay.Sunrise,
                _ => TimeOfDay.Unknown
            };
        }
    }
}
