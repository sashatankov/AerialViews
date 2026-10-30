using System.IO;
using Newtonsoft.Json;

namespace AerialViews.Models
{
    public class VideoAsset
    {
        [JsonProperty("id")]
        public string Id { get; set; } = "";

        [JsonProperty("accessibilityLabel")]
        public string AccessibilityLabel { get; set; } = "";

        [JsonProperty("title")]
        public string? Title { get; set; }

        [JsonProperty("shotID")]
        public string? ShotId { get; set; }

        [JsonProperty("timeOfDay")]
        public string TimeOfDayRaw { get; set; } = "";

        [JsonProperty("scene")]
        public string SceneRaw { get; set; } = "";

        [JsonProperty("url-1080-H264")]
        public string? Url1080H264 { get; set; }

        [JsonProperty("url-1080-SDR")]
        public string? Url1080SDR { get; set; }

        [JsonProperty("url-1080-HDR")]
        public string? Url1080HDR { get; set; }

        [JsonProperty("url-4K-SDR")]
        public string? Url4KSDR { get; set; }

        [JsonProperty("url-4K-HDR")]
        public string? Url4KHDR { get; set; }

        [JsonProperty("url-4K-SDR-240FPS")]
        public string? Url4KSDR240FPS { get; set; }

        [JsonProperty("pointsOfInterest")]
        public Dictionary<string, string>? PointsOfInterest { get; set; }

        [JsonProperty("categories")]
        public List<string>? Categories { get; set; }

        public TimeOfDay TimeOfDay => EnumHelpers.ParseTimeOfDay(TimeOfDayRaw);
        public SceneType Scene => EnumHelpers.ParseSceneType(SceneRaw);

        public string? GetUrlForQuality(VideoQuality quality, AerialMediaSource source)
        {
            return source switch
            {
                AerialMediaSource.Apple => quality switch
                {
                    VideoQuality.Video1080SDR => Url1080SDR,
                    VideoQuality.Video1080HDR => Url1080HDR,
                    VideoQuality.Video4KSDR => Url4KSDR,
                    VideoQuality.Video4KHDR => Url4KHDR,
                    _ => Url1080H264
                },
                AerialMediaSource.Amazon => quality switch
                {
                    VideoQuality.Video1080SDR => Url1080SDR,
                    VideoQuality.Video4KSDR => Url4KSDR,
                    _ => Url1080SDR
                },
                AerialMediaSource.Comm1 => quality switch
                {
                    VideoQuality.Video1080SDR => Url1080SDR,
                    VideoQuality.Video4KSDR => Url4KSDR,
                    _ => Url1080H264
                },
                AerialMediaSource.Comm2 => quality switch
                {
                    VideoQuality.Video1080SDR => Url1080HDR, // Map HDR to SDR as 10bit colour space alone is not HDR
                    VideoQuality.Video4KSDR => Url4KSDR,
                    _ => Url1080H264
                },
                _ => Url1080SDR
            };
        }

        public string GetUrl(VideoQuality quality, AerialMediaSource source)
        {
            var url = new[]
            {
                GetUrlForQuality(quality, source), Url1080SDR, Url1080H264,
                source == AerialMediaSource.Apple ? Url4KSDR240FPS : null
            }.FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate)) ?? "";
            // Apple uses invalid certificate, switch to http
            if (source == AerialMediaSource.Apple)
            {
                url = url.Replace("https://", "http://");
            }
            return url;
        }

        public string Description => !string.IsNullOrEmpty(Title) ? Title : AccessibilityLabel;

        public List<string> AllUrls()
        {
            var urls = new HashSet<string>();
            if (!string.IsNullOrEmpty(Url1080H264)) urls.Add(Path.GetFileNameWithoutExtension(Url1080H264).ToLower());
            if (!string.IsNullOrEmpty(Url1080SDR)) urls.Add(Path.GetFileNameWithoutExtension(Url1080SDR).ToLower());
            if (!string.IsNullOrEmpty(Url1080HDR)) urls.Add(Path.GetFileNameWithoutExtension(Url1080HDR).ToLower());
            if (!string.IsNullOrEmpty(Url4KSDR)) urls.Add(Path.GetFileNameWithoutExtension(Url4KSDR).ToLower());
            if (!string.IsNullOrEmpty(Url4KHDR)) urls.Add(Path.GetFileNameWithoutExtension(Url4KHDR).ToLower());
            if (!string.IsNullOrEmpty(Url4KSDR240FPS)) urls.Add(Path.GetFileNameWithoutExtension(Url4KSDR240FPS).ToLower());
            return urls.ToList();
        }
    }

    public class VideoManifest
    {
        [JsonProperty("assets")]
        public List<VideoAsset>? Assets { get; set; }

        [JsonProperty("version")]
        public int Version { get; set; }

        [JsonProperty("initialAssetCount")]
        public int InitialAssetCount { get; set; }
    }

    public class AerialMedia
    {
        public string Uri { get; set; } = "";
        public AerialMediaType Type { get; set; } = AerialMediaType.Video;
        public AerialMediaSource Source { get; set; }
        public TimeOfDay TimeOfDay { get; set; }
        public SceneType Scene { get; set; }
        public string Description { get; set; } = "";
        public Dictionary<string, string>? PointsOfInterest { get; set; }
    }
}
