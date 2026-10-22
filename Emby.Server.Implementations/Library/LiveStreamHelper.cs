#pragma warning disable CS1591

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Extensions.Json;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Extensions;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.Dlna;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.MediaInfo;
using Microsoft.Extensions.Logging;

namespace Emby.Server.Implementations.Library
{
    public class LiveStreamHelper
    {
        private readonly IMediaEncoder _mediaEncoder;
        private readonly ILogger _logger;
        private readonly IApplicationPaths _appPaths;
        private readonly JsonSerializerOptions _jsonOptions = JsonDefaults.Options;

        public LiveStreamHelper(IMediaEncoder mediaEncoder, ILogger logger, IApplicationPaths appPaths)
        {
            _mediaEncoder = mediaEncoder;
            _logger = logger;
            _appPaths = appPaths;
        }

        public async Task AddMediaInfoWithProbe(MediaSourceInfo mediaSource, LiveStreamProbeOptions probeOptions, CancellationToken cancellationToken)
        {
            var originalRuntime = mediaSource.RunTimeTicks;

            var now = DateTime.UtcNow;

            var cacheFilePath = string.IsNullOrEmpty(probeOptions.CacheKey) ? null : Path.Combine(_appPaths.CachePath, "mediainfo", probeOptions.CacheKey.GetMD5().ToString("N", CultureInfo.InvariantCulture) + ".json");

            MediaInfo? mediaInfo = await ReadCachedMediaInfo(cacheFilePath, cancellationToken).ConfigureAwait(false);

            if (mediaInfo is null)
            {
                await DelayBeforeProbe(probeOptions.AddProbeDelay, mediaSource, cancellationToken).ConfigureAwait(false);

                mediaSource.AnalyzeDurationMs = 3000;

                mediaInfo = await _mediaEncoder.GetMediaInfo(
                    new MediaInfoRequest
                    {
                        MediaSource = mediaSource,
                        MediaType = probeOptions.IsAudio ? DlnaProfileType.Audio : DlnaProfileType.Video,
                        ExtractChapters = false
                    },
                    cancellationToken).ConfigureAwait(false);

                await SaveMediaInfoToCache(cacheFilePath, mediaInfo, cancellationToken).ConfigureAwait(false);
            }

            var mediaStreams = SelectCachedStreams(mediaInfo.MediaStreams, probeOptions.CacheKey);

            _logger.LogInformation("Live tv media info probe took {0} seconds", (DateTime.UtcNow - now).TotalSeconds.ToString(CultureInfo.InvariantCulture));

            mediaSource.Bitrate = mediaInfo.Bitrate;
            mediaSource.Container = mediaInfo.Container;
            mediaSource.Formats = mediaInfo.Formats;
            mediaSource.MediaStreams = mediaStreams;
            mediaSource.RunTimeTicks = mediaInfo.RunTimeTicks;
            mediaSource.Size = mediaInfo.Size;
            mediaSource.Timestamp = mediaInfo.Timestamp;
            mediaSource.Video3DFormat = mediaInfo.Video3DFormat;
            mediaSource.VideoType = mediaInfo.VideoType;

            mediaSource.DefaultSubtitleStreamIndex = null;

            // Null this out so that it will be treated like a live stream
            if (!originalRuntime.HasValue)
            {
                mediaSource.RunTimeTicks = null;
            }

            var audioStream = mediaStreams.FirstOrDefault(i => i.Type == MediaStreamType.Audio);

            if (audioStream is null || audioStream.Index == -1)
            {
                mediaSource.DefaultAudioStreamIndex = null;
            }
            else
            {
                mediaSource.DefaultAudioStreamIndex = audioStream.Index;
            }

            var videoStream = mediaStreams.FirstOrDefault(i => i.Type == MediaStreamType.Video);
            NormalizeLiveVideoStream(videoStream);

            mediaSource.AnalyzeDurationMs = 3000;

            // Try to estimate this
            mediaSource.InferTotalBitrate(true);
        }

        public Task AddMediaInfoWithProbe(MediaSourceInfo mediaSource, bool isAudio, bool addProbeDelay, CancellationToken cancellationToken)
        {
            return AddMediaInfoWithProbe(mediaSource, new LiveStreamProbeOptions(isAudio, null, addProbeDelay), cancellationToken);
        }

        private async Task<MediaInfo?> ReadCachedMediaInfo(string? cacheFilePath, CancellationToken cancellationToken)
        {
            MediaInfo? mediaInfo = null;

            if (cacheFilePath is not null)
            {
                try
                {
                    FileStream jsonStream = AsyncFile.OpenRead(cacheFilePath);

                    await using (jsonStream.ConfigureAwait(false))
                    {
                        mediaInfo = await JsonSerializer.DeserializeAsync<MediaInfo>(jsonStream, _jsonOptions, cancellationToken).ConfigureAwait(false);
                        // _logger.LogDebug("Found cached media info");
                    }
                }
                catch (IOException ex)
                {
                    _logger.LogDebug(ex, "Could not open cached media info");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error opening cached media info");
                }
            }

            return mediaInfo;
        }

        private async Task DelayBeforeProbe(bool addProbeDelay, MediaSourceInfo mediaSource, CancellationToken cancellationToken)
        {
            if (addProbeDelay)
            {
                var delayMs = mediaSource.AnalyzeDurationMs ?? 0;
                delayMs = Math.Max(3000, delayMs);
                _logger.LogInformation("Waiting {0}ms before probing the live stream", delayMs);
                await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task SaveMediaInfoToCache(string? cacheFilePath, MediaInfo mediaInfo, CancellationToken cancellationToken)
        {
            if (cacheFilePath is not null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(cacheFilePath) ?? throw new InvalidOperationException("Path can't be a root directory."));
                FileStream createStream = AsyncFile.OpenWrite(cacheFilePath);
                await using (createStream.ConfigureAwait(false))
                {
                    await JsonSerializer.SerializeAsync(createStream, mediaInfo, _jsonOptions, cancellationToken).ConfigureAwait(false);
                }

                _logger.LogDebug("Saved media info to {0}", cacheFilePath);
            }
        }

        private static IReadOnlyList<MediaStream> SelectCachedStreams(IReadOnlyList<MediaStream> mediaStreams, string? cacheKey)
        {
            if (!string.IsNullOrEmpty(cacheKey))
            {
                var newList = new List<MediaStream>();
                newList.AddRange(mediaStreams.Where(i => i.Type == MediaStreamType.Video).Take(1));
                newList.AddRange(mediaStreams.Where(i => i.Type == MediaStreamType.Audio).Take(1));

                foreach (var stream in newList)
                {
                    stream.Index = -1;
                    stream.Language = null;
                }

                mediaStreams = newList;
            }

            return mediaStreams;
        }

        private static void NormalizeLiveVideoStream(MediaStream? videoStream)
        {
            if (videoStream is not null)
            {
                if (!videoStream.BitRate.HasValue)
                {
                    var width = videoStream.Width ?? 1920;

                    if (width >= 3000)
                    {
                        videoStream.BitRate = 30000000;
                    }
                    else if (width >= 1900)
                    {
                        videoStream.BitRate = 20000000;
                    }
                    else if (width >= 1200)
                    {
                        videoStream.BitRate = 8000000;
                    }
                    else if (width >= 700)
                    {
                        videoStream.BitRate = 2000000;
                    }
                }

                // This is coming up false and preventing stream copy
                videoStream.IsAVC = null;
            }
        }

#pragma warning disable CA1034 // Parameter object of AddMediaInfoWithProbe; nested to keep it with its only user
        public readonly record struct LiveStreamProbeOptions(bool IsAudio, string? CacheKey, bool AddProbeDelay);
#pragma warning restore CA1034
    }
}
