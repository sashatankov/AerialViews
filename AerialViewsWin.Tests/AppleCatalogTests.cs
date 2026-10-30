using System.IO;
using AerialViews.Models;
using AerialViews.Providers;
using AerialViews.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace AerialViewsWin.Tests
{
    public class AppleCatalogTests
    {
        private static string ReadResource(string name)
        {
            var assembly = typeof(VideoProvider).Assembly;
            var resource = Assert.Single(assembly.GetManifestResourceNames(), n => n.EndsWith("." + name));
            using var stream = assembly.GetManifestResourceStream(resource)!;
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        private static List<VideoAsset> LoadAssets() =>
            JsonConvert.DeserializeObject<VideoManifest>(ReadResource(VideoProvider.AppleManifestResource))!.Assets!;

        private static ScreensaverSettings AppleOnlySettings()
        {
            var settings = new ScreensaverSettings();
            settings.AmazonSource.Enabled = false;
            settings.Comm1Source.Enabled = false;
            settings.Comm2Source.Enabled = false;
            settings.Playlist.ShuffleVideos = false;
            return settings;
        }

        public static IEnumerable<object[]> Qualities => Enum.GetValues<VideoQuality>().Select(q => new object[] { q });

        [Fact]
        public void LoadCatalog_CombinedSources_PreservesLegacyAssetsAndAdds42UniqueVideos()
        {
            var catalog = JObject.Parse(ReadResource(VideoProvider.AppleManifestResource));
            var assets = (JArray)catalog["assets"]!;
            var byId = assets.ToDictionary(a => (string)a["id"]!);
            Assert.Equal(156, byId.Count);
            Assert.Equal(156, VideoProvider.CreateApple().AssetCount);
            Assert.Equal(156, (int)catalog["initialAssetCount"]!);

            var legacy = (JArray)JObject.Parse(ReadResource("tvos15.json"))["assets"]!;
            Assert.Equal(114, legacy.Count);
            foreach (var original in legacy.Cast<JObject>())
            {
                var merged = byId[(string)original["id"]!];
                foreach (var property in original.Properties())
                {
                    Assert.True(JToken.DeepEquals(property.Value, merged[property.Name]),
                        $"Legacy asset {original["id"]}: {property.Name} changed");
                }
            }

            Assert.Equal(new[] { 114, 20, 3, 15, 4 },
                catalog["sources"]!.Select(s => (int)s["addedCount"]!));
            Assert.Equal(42, LoadAssets().Count(a => a.Url1080SDR == null && a.Url1080H264 == null));
            Assert.Equal(3, LoadAssets().Count(a => a.Description.StartsWith("Sequoia ")));
            Assert.Equal(4, LoadAssets().Count(a => a.Description.StartsWith("Tahoe ")));
            Assert.Equal(4, LoadAssets().Count(a => a.ShotId?.StartsWith("GG_A_") == true));
            Assert.Contains(LoadAssets(), a => a.Description == "Sonoma Evening");
        }

        [Fact]
        public void LoadCatalog_AllAssets_HaveDescriptionsClassificationsAndResolvedPointsOfInterest()
        {
            var strings = JsonConvert.DeserializeObject<Dictionary<string, string>>(
                ReadResource(VideoProvider.AppleStringsResource))!;
            var (_, metadata) = VideoProvider.CreateApple().LoadMedia(new SourceSettings());
            foreach (var asset in LoadAssets())
            {
                Assert.False(string.IsNullOrWhiteSpace(asset.Description));
                Assert.NotEqual(SceneType.Unknown, asset.Scene);
                Assert.NotEqual(TimeOfDay.Unknown, asset.TimeOfDay);
                Assert.NotEmpty(asset.AllUrls());
                foreach (var urlKey in asset.AllUrls())
                {
                    Assert.True(metadata.ContainsKey(urlKey));
                    foreach (var poi in asset.PointsOfInterest ?? new())
                    {
                        Assert.False(string.IsNullOrWhiteSpace(strings[poi.Value]));
                        Assert.Equal(strings[poi.Value], metadata[urlKey].Item2[poi.Key]);
                    }
                }
            }
        }

        [Theory]
        [MemberData(nameof(Qualities))]
        public void GetUrl_EveryQuality_PreservesLegacySelectionAndIncludes4KOnlyVideos(VideoQuality quality)
        {
            var oldAssets = JsonConvert.DeserializeObject<VideoManifest>(ReadResource("tvos15.json"))!.Assets!;
            var assets = LoadAssets();
            foreach (var oldAsset in oldAssets)
            {
                var combined = Assert.Single(assets, a => a.Id == oldAsset.Id);
                Assert.Equal(oldAsset.GetUrl(quality, AerialMediaSource.Apple), combined.GetUrl(quality, AerialMediaSource.Apple));
            }
            foreach (var asset in assets)
            {
                Assert.True(Uri.TryCreate(asset.GetUrl(quality, AerialMediaSource.Apple), UriKind.Absolute, out var uri));
                Assert.Equal("sylvan.apple.com", uri!.Host);
                Assert.EndsWith(".mov", uri.AbsolutePath);
                if (asset.Url1080SDR == null && asset.Url1080H264 == null)
                {
                    Assert.Equal(asset.Url4KSDR240FPS!.Replace("https://", "http://"), uri.AbsoluteUri);
                }
            }
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" ")]
        public void GetUrl_MissingOrBlankRequestedFormat_Uses4KFallbackOnlyForApple(string? missing)
        {
            var asset = new VideoAsset { Url1080SDR = missing, Url4KSDR240FPS = "https://sylvan.apple.com/new.mov" };
            Assert.Equal("http://sylvan.apple.com/new.mov", asset.GetUrl(VideoQuality.Video1080SDR, AerialMediaSource.Apple));
            Assert.Empty(asset.GetUrl(VideoQuality.Video1080SDR, AerialMediaSource.Amazon));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void BuildPlaylist_DuplicateOption_MatchesAppleDownloadSelection(bool removeDuplicates)
        {
            foreach (var quality in Enum.GetValues<VideoQuality>())
            {
                var settings = AppleOnlySettings();
                settings.Playlist.RemoveDuplicates = removeDuplicates;
                settings.AppleSource.Quality = quality;
                // These resource constants are also used by the settings download handler.
                var downloadProvider = new VideoProvider(VideoProvider.AppleManifestResource,
                    VideoProvider.AppleStringsResource, AerialMediaSource.Apple);
                var (downloads, _) = downloadProvider.LoadMedia(settings.AppleSource);
                var playlist = new PlaylistManager();
                playlist.BuildPlaylist(settings);
                Assert.Equal(156, downloads.Count);
                Assert.Equal(downloads.Count, playlist.Count);
                Assert.Equal(downloads.Select(m => m.Uri), Enumerable.Range(0, playlist.Count).Select(_ => playlist.GetNext()!.Uri));
            }
        }

        [Fact]
        public void LoadMedia_DisabledOrRestrictedFilters_RespectsAppleSettings()
        {
            var provider = VideoProvider.CreateApple();
            Assert.Empty(provider.LoadMedia(new SourceSettings { Enabled = false }).media);
            Assert.Empty(provider.LoadMedia(new SourceSettings { Scenes = new() }).media);
            Assert.Empty(provider.LoadMedia(new SourceSettings { TimesOfDay = new() }).media);
            var settings = new SourceSettings
            {
                Scenes = new() { SceneType.Countryside },
                TimesOfDay = new() { TimeOfDay.Night }
            };
            var (media, _) = provider.LoadMedia(settings);
            Assert.Contains(media, m => m.Description == "Golden Gate Night");
            Assert.All(media, m =>
            {
                Assert.Equal(SceneType.Countryside, m.Scene);
                Assert.Equal(TimeOfDay.Night, m.TimeOfDay);
            });
            var playlistSettings = AppleOnlySettings();
            playlistSettings.AppleSource.Enabled = false;
            var playlist = new PlaylistManager();
            playlist.BuildPlaylist(playlistSettings);
            Assert.Equal(0, playlist.Count);
        }

        [Fact]
        public void GetDescription_New4KVideo_ResolvesTranslatedPoiAndKeepsAppleCachePath()
        {
            var playlist = new PlaylistManager();
            playlist.BuildPlaylist(AppleOnlySettings());
            var videos = Enumerable.Range(0, playlist.Count).Select(_ => playlist.GetNext()!).ToList();
            var video = Assert.Single(videos, m => m.Description == "Sonoma Clouds");
            var (_, metadata) = VideoProvider.CreateApple().LoadMedia(new SourceSettings());
            var expected = metadata[Path.GetFileNameWithoutExtension(video.Uri).ToLowerInvariant()].Item2["0"];
            Assert.Equal(expected, playlist.GetDescription(video, 0));
            Assert.Equal(expected, playlist.GetDescription(video, 30));
            Assert.Equal(Path.Combine(VideoCacheManager.GetSourceCachePath(AerialMediaSource.Apple),
                Path.GetFileName(new Uri(video.Uri).AbsolutePath)),
                VideoCacheManager.GetCachedFilePath(video.Uri, AerialMediaSource.Apple));
        }
    }
}
