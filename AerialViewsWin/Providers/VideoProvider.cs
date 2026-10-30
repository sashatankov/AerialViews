using System.IO;
using System.Reflection;
using AerialViews.Models;
using Newtonsoft.Json;

namespace AerialViews.Providers
{
    public class VideoProvider
    {
        private readonly string _manifestResource;
        private readonly string _stringsResource;
        private readonly AerialMediaSource _source;

        public VideoProvider(string manifestResource, string stringsResource, AerialMediaSource source)
        {
            _manifestResource = manifestResource;
            _stringsResource = stringsResource;
            _source = source;
        }

        public (List<AerialMedia> media, Dictionary<string, Tuple<string, Dictionary<string, string>>> metadata) LoadMedia(SourceSettings settings)
        {
            var media = new List<AerialMedia>();
            var metadata = new Dictionary<string, Tuple<string, Dictionary<string, string>>>();

            if (!settings.Enabled) return (media, metadata);

            var manifest = LoadManifest();
            var strings = LoadStrings();

            if (manifest?.Assets == null) return (media, metadata);

            foreach (var asset in manifest.Assets)
            {
                var timeOfDay = asset.TimeOfDay;
                var scene = asset.Scene;

                bool timeMatch = settings.TimesOfDay.Contains(timeOfDay) || timeOfDay == TimeOfDay.Unknown;
                bool sceneMatch = settings.Scenes.Contains(scene) || scene == SceneType.Unknown;

                if (timeMatch && sceneMatch)
                {
                    var url = asset.GetUrl(settings.Quality, _source);
                    if (!string.IsNullOrEmpty(url))
                    {
                        media.Add(new AerialMedia
                        {
                            Uri = url,
                            Type = AerialMediaType.Video,
                            Source = _source,
                            TimeOfDay = timeOfDay,
                            Scene = scene,
                            Description = asset.Description,
                            PointsOfInterest = asset.PointsOfInterest
                        });
                    }
                }

                // Build metadata for all URLs
                var poiTranslated = new Dictionary<string, string>();
                if (asset.PointsOfInterest != null)
                {
                    foreach (var poi in asset.PointsOfInterest)
                    {
                        if (strings != null && strings.TryGetValue(poi.Value, out var translated))
                        {
                            poiTranslated[poi.Key] = translated;
                        }
                        else
                        {
                            poiTranslated[poi.Key] = asset.Description;
                        }
                    }
                }

                var data = Tuple.Create(asset.Description, poiTranslated);
                foreach (var urlKey in asset.AllUrls())
                {
                    metadata[urlKey] = data;
                }
            }

            return (media, metadata);
        }

        private VideoManifest? LoadManifest()
        {
            var json = LoadEmbeddedResource(_manifestResource);
            if (json == null) return null;
            return JsonConvert.DeserializeObject<VideoManifest>(json);
        }

        private Dictionary<string, string>? LoadStrings()
        {
            var json = LoadEmbeddedResource(_stringsResource);
            if (json == null) return null;
            return JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
        }

        private static string? LoadEmbeddedResource(string resourceName)
        {
            var assembly = Assembly.GetExecutingAssembly();
            var fullName = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith(resourceName, StringComparison.OrdinalIgnoreCase));

            if (fullName == null) return null;

            using var stream = assembly.GetManifestResourceStream(fullName);
            if (stream == null) return null;

            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }

    public class PlaylistManager
    {
        private List<AerialMedia> _playlist = new();
        private Dictionary<string, Tuple<string, Dictionary<string, string>>> _metadata = new();
        private int _currentIndex = -1;
        private readonly Random _random = new();

        public int Count => _playlist.Count;
        public AerialMedia? Current => _currentIndex >= 0 && _currentIndex < _playlist.Count ? _playlist[_currentIndex] : null;

        public void BuildPlaylist(ScreensaverSettings settings)
        {
            var allMedia = new List<AerialMedia>();
            var allMetadata = new Dictionary<string, Tuple<string, Dictionary<string, string>>>();

            var providers = new[]
            {
                (new VideoProvider("tvos15.json", "tvos15_strings.json", AerialMediaSource.Apple), settings.AppleSource),
                (new VideoProvider("fireos8.json", "fireos8_strings.json", AerialMediaSource.Amazon), settings.AmazonSource),
                (new VideoProvider("comm1.json", "comm1_strings.json", AerialMediaSource.Comm1), settings.Comm1Source),
                (new VideoProvider("comm2.json", "comm2_strings.json", AerialMediaSource.Comm2), settings.Comm2Source),
            };

            foreach (var (provider, sourceSettings) in providers)
            {
                var (media, metadata) = provider.LoadMedia(sourceSettings);
                allMedia.AddRange(media);
                foreach (var kvp in metadata)
                {
                    allMetadata.TryAdd(kvp.Key, kvp.Value);
                }
            }

            if (settings.Playlist.RemoveDuplicates)
            {
                allMedia = allMedia.GroupBy(m => m.Uri).Select(g => g.First()).ToList();
            }

            if (settings.Playlist.ShuffleVideos)
            {
                Shuffle(allMedia);
            }

            _playlist = allMedia;
            _metadata = allMetadata;
            _currentIndex = -1;
        }

        public AerialMedia? GetNext()
        {
            if (_playlist.Count == 0) return null;
            _currentIndex = (_currentIndex + 1) % _playlist.Count;
            return _playlist[_currentIndex];
        }

        public AerialMedia? GetPrevious()
        {
            if (_playlist.Count == 0) return null;
            _currentIndex = (_currentIndex - 1 + _playlist.Count) % _playlist.Count;
            return _playlist[_currentIndex];
        }

        public string? GetDescription(AerialMedia media, int positionSeconds)
        {
            // Try to find POI-based description from metadata
            var filename = Path.GetFileNameWithoutExtension(media.Uri)?.ToLower();
            if (filename != null && _metadata.TryGetValue(filename, out var data))
            {
                // Find the closest POI at or before the current position
                if (data.Item2.Count > 0)
                {
                    string? bestDesc = null;
                    int bestTime = -1;
                    foreach (var poi in data.Item2)
                    {
                        if (int.TryParse(poi.Key, out int poiTime) && poiTime <= positionSeconds && poiTime > bestTime)
                        {
                            bestTime = poiTime;
                            bestDesc = poi.Value;
                        }
                    }
                    if (bestDesc != null) return bestDesc;
                }
                // Fallback to common description
                return data.Item1;
            }

            return media.Description;
        }

        private void Shuffle<T>(List<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = _random.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
