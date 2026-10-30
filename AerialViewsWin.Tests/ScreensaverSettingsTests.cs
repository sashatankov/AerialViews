using AerialViews.Models;
using AerialViews.Providers;
using Xunit;

namespace AerialViewsWin.Tests
{
    public class ScreensaverSettingsTests : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "AerialViewsTests", Guid.NewGuid().ToString("N"));
        private string SettingsPath => Path.Combine(_directory, "settings.json");

        [Fact]
        public void Save_DisabledAmazon_RemainsDisabledOnReloadAndExcludedFromPlaylist()
        {
            var settings = new ScreensaverSettings();
            settings.AmazonSource.Enabled = false;
            settings.Save(SettingsPath);

            var loaded = ScreensaverSettings.Load(SettingsPath);
            Assert.False(loaded.AmazonSource.Enabled);
            var playlist = new PlaylistManager();
            playlist.BuildPlaylist(loaded);
            Assert.True(playlist.Count > 0);
            for (int i = 0; i < playlist.Count; i++)
            {
                Assert.NotEqual(AerialMediaSource.Amazon, playlist.GetNext()!.Source);
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Save_RestrictedFilters_ReloadsExactSelections(bool empty)
        {
            var settings = new ScreensaverSettings();
            settings.AppleSource.Scenes = empty ? new() : new() { SceneType.City };
            settings.AppleSource.TimesOfDay = empty ? new() : new() { TimeOfDay.Night };
            settings.Save(SettingsPath);

            var loaded = ScreensaverSettings.Load(SettingsPath);
            Assert.True(settings.AppleSource.Scenes.SetEquals(loaded.AppleSource.Scenes));
            Assert.True(settings.AppleSource.TimesOfDay.SetEquals(loaded.AppleSource.TimesOfDay));
        }

        [Fact]
        public void Save_UpdatedPreferences_ReplacesPreviousSettings()
        {
            var settings = new ScreensaverSettings();
            settings.Save(SettingsPath);
            settings.AmazonSource.Enabled = false;
            settings.Overlays.MessageLine1 = "Saved message";
            settings.Appearance.VideoBrightness = 72;
            settings.Playlist.PlaybackSpeed = 0.5;
            settings.Save(SettingsPath);

            var loaded = ScreensaverSettings.Load(SettingsPath);
            Assert.False(loaded.AmazonSource.Enabled);
            Assert.Equal("Saved message", loaded.Overlays.MessageLine1);
            Assert.Equal(72, loaded.Appearance.VideoBrightness);
            Assert.Equal(0.5, loaded.Playlist.PlaybackSpeed);
            Assert.Single(Directory.GetFiles(_directory));
        }

        [Fact]
        public void Save_FileLocked_ReportsFailureAndPreservesPreviousSettings()
        {
            var settings = new ScreensaverSettings();
            settings.Save(SettingsPath);
            var original = File.ReadAllText(SettingsPath);
            using (var lockedFile = new FileStream(SettingsPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                settings.AmazonSource.Enabled = false;
                var exception = Record.Exception(() => settings.Save(SettingsPath));
                Assert.True(exception is IOException or UnauthorizedAccessException,
                    $"Expected a file access failure, received: {exception}");
            }
            Assert.Equal(original, File.ReadAllText(SettingsPath));
            Assert.Single(Directory.GetFiles(_directory));
        }

        [Fact]
        public void Load_MissingSettings_UsesDefaultsWithoutWritingFile()
        {
            var loaded = ScreensaverSettings.Load(SettingsPath);
            Assert.True(loaded.AmazonSource.Enabled);
            Assert.False(File.Exists(SettingsPath));
        }

        public void Dispose()
        {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        }
    }
}
