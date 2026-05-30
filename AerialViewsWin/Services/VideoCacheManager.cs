using System.Diagnostics;
using System.IO;
using System.Net.Http;
using AerialViews.Models;

namespace AerialViews.Services
{
    public class VideoCacheManager
    {
        private static readonly HttpClient _httpClient = new()
        {
            Timeout = TimeSpan.FromMinutes(30)
        };

        private static readonly string _cacheRoot;

        static VideoCacheManager()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _cacheRoot = Path.Combine(appData, "AerialViews", "Cache");
        }

        /// <summary>
        /// Gets the cache subdirectory for a given source.
        /// </summary>
        public static string GetSourceCachePath(AerialMediaSource source)
        {
            var subDir = source switch
            {
                AerialMediaSource.Apple => "Apple",
                AerialMediaSource.Amazon => "Amazon",
                AerialMediaSource.Comm1 => "Comm1",
                AerialMediaSource.Comm2 => "Comm2",
                _ => "Other"
            };
            return Path.Combine(_cacheRoot, subDir);
        }

        /// <summary>
        /// Gets the expected local file path for a remote video URL.
        /// </summary>
        public static string GetCachedFilePath(string remoteUrl, AerialMediaSource source)
        {
            var fileName = Path.GetFileName(new Uri(remoteUrl).AbsolutePath);
            return Path.Combine(GetSourceCachePath(source), fileName);
        }

        /// <summary>
        /// Checks whether a video is already cached locally.
        /// </summary>
        public static bool IsCached(string remoteUrl, AerialMediaSource source)
        {
            var localPath = GetCachedFilePath(remoteUrl, source);
            return File.Exists(localPath);
        }

        /// <summary>
        /// Returns the local file path if cached, otherwise returns the original remote URL.
        /// </summary>
        public static string ResolvePlaybackUri(string remoteUrl, AerialMediaSource source)
        {
            var localPath = GetCachedFilePath(remoteUrl, source);
            return File.Exists(localPath) ? localPath : remoteUrl;
        }

        /// <summary>
        /// Downloads a single video to the cache directory.
        /// </summary>
        public static async Task<bool> DownloadVideoAsync(
            string remoteUrl,
            AerialMediaSource source,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var localPath = GetCachedFilePath(remoteUrl, source);
            var dir = Path.GetDirectoryName(localPath)!;
            Directory.CreateDirectory(dir);

            // Skip if already downloaded
            if (File.Exists(localPath)) return true;

            var tempPath = localPath + ".tmp";

            try
            {
                using var response = await _httpClient.GetAsync(remoteUrl,
                    HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? -1;
                long downloadedBytes = 0;

                using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write,
                    FileShare.None, 81920, useAsync: true);

                var buffer = new byte[81920];
                int bytesRead;

                while ((bytesRead = await contentStream.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                    downloadedBytes += bytesRead;

                    if (totalBytes > 0)
                    {
                        progress?.Report((double)downloadedBytes / totalBytes);
                    }
                }

                // Rename temp file to final name
                File.Move(tempPath, localPath, overwrite: true);
                return true;
            }
            catch (OperationCanceledException)
            {
                // Clean up partial download
                TryDeleteFile(tempPath);
                return false;
            }
            catch
            {
                TryDeleteFile(tempPath);
                return false;
            }
        }

        /// <summary>
        /// Downloads all videos in the list, reporting overall progress.
        /// </summary>
        public static async Task<(int succeeded, int failed)> DownloadAllAsync(
            List<AerialMedia> videos,
            IProgress<(int current, int total, string name, double fileProgress)>? progress = null,
            CancellationToken cancellationToken = default)
        {
            int succeeded = 0;
            int failed = 0;
            int total = videos.Count;

            for (int i = 0; i < videos.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var video = videos[i];
                var name = video.Description;
                int index = i + 1;

                var fileProgress = new Progress<double>(p =>
                {
                    progress?.Report((index, total, name, p));
                });

                bool ok = await DownloadVideoAsync(video.Uri, video.Source, fileProgress, cancellationToken);

                if (ok) succeeded++;
                else failed++;
            }

            return (succeeded, failed);
        }

        /// <summary>
        /// Calculates the total cache size in bytes.
        /// </summary>
        public static long GetCacheSize()
        {
            if (!Directory.Exists(_cacheRoot)) return 0;
            return new DirectoryInfo(_cacheRoot)
                .EnumerateFiles("*", SearchOption.AllDirectories)
                .Sum(f => f.Length);
        }

        /// <summary>
        /// Calculates the cache size for a specific source in bytes.
        /// </summary>
        public static long GetSourceCacheSize(AerialMediaSource source)
        {
            var path = GetSourceCachePath(source);
            if (!Directory.Exists(path)) return 0;
            return new DirectoryInfo(path)
                .EnumerateFiles("*", SearchOption.AllDirectories)
                .Sum(f => f.Length);
        }

        /// <summary>
        /// Returns a count of cached files for a specific source.
        /// </summary>
        public static int GetSourceCacheCount(AerialMediaSource source)
        {
            var path = GetSourceCachePath(source);
            if (!Directory.Exists(path)) return 0;
            return new DirectoryInfo(path)
                .EnumerateFiles("*", SearchOption.AllDirectories)
                .Count();
        }

        /// <summary>
        /// Clears the cache. If source is null, clears everything.
        /// </summary>
        public static void ClearCache(AerialMediaSource? source = null)
        {
            try
            {
                if (source == null)
                {
                    if (Directory.Exists(_cacheRoot))
                        Directory.Delete(_cacheRoot, recursive: true);
                }
                else
                {
                    var path = GetSourceCachePath(source.Value);
                    if (Directory.Exists(path))
                        Directory.Delete(path, recursive: true);
                }
            }
            catch
            {
                // Silently fail — files may be in use
            }
        }

        /// <summary>
        /// Opens the cache folder in Windows Explorer.
        /// </summary>
        public static void OpenCacheFolder()
        {
            Directory.CreateDirectory(_cacheRoot);
            Process.Start(new ProcessStartInfo
            {
                FileName = _cacheRoot,
                UseShellExecute = true
            });
        }

        /// <summary>
        /// Formats a byte count as a human-readable string.
        /// </summary>
        public static string FormatSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
            return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
        }

        private static void TryDeleteFile(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch { /* ignore */ }
        }
    }
}
