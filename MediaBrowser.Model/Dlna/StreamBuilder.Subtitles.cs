using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Extensions;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Extensions;
using MediaBrowser.Model.MediaInfo;
using MediaBrowser.Model.Session;
using Microsoft.Extensions.Logging;

namespace MediaBrowser.Model.Dlna
{
    /// <summary>
    /// Class StreamBuilder.
    /// </summary>
    public partial class StreamBuilder
    {
        private static int? GetDefaultSubtitleStreamIndex(MediaSourceInfo item, SubtitleProfile[] subtitleProfiles)
        {
            int highestScore = GetHighestSubtitleScore(item);

            List<MediaStream> topStreams = GetTopScoringSubtitleStreams(item, highestScore);

            return SelectMostEfficientSubtitleStreamIndex(item, subtitleProfiles, topStreams);
        }

        private static int GetHighestSubtitleScore(MediaSourceInfo item)
        {
            int highestScore = -1;
            foreach (var stream in item.MediaStreams)
            {
                if (stream.Type == MediaStreamType.Subtitle
                    && IsScoreAbove(stream.Score, highestScore))
                {
                    highestScore = stream.Score.Value;
                }
            }

            return highestScore;
        }

        private static bool IsScoreAbove([NotNullWhen(true)] int? score, int highestScore)
        {
            return score.HasValue && score.Value > highestScore;
        }

        private static List<MediaStream> GetTopScoringSubtitleStreams(MediaSourceInfo item, int highestScore)
        {
            List<MediaStream> topStreams = [];
            foreach (var stream in item.MediaStreams)
            {
                if (IsSubtitleWithScore(stream, highestScore))
                {
                    topStreams.Add(stream);
                }
            }

            return topStreams;
        }

        private static bool IsSubtitleWithScore(MediaStream stream, int highestScore)
        {
            return stream.Type == MediaStreamType.Subtitle && stream.Score.HasValue && stream.Score.Value == highestScore;
        }

        private static int? SelectMostEfficientSubtitleStreamIndex(MediaSourceInfo item, SubtitleProfile[] subtitleProfiles, List<MediaStream> topStreams)
        {
            // If multiple streams have an equal score, try to pick the most efficient one
            if (topStreams.Count <= 1)
            {
                return item.DefaultSubtitleStreamIndex;
            }

            foreach (var stream in topStreams)
            {
                foreach (var profile in subtitleProfiles)
                {
                    if (IsEfficientExternalSubtitleProfile(profile, stream))
                    {
                        return stream.Index;
                    }
                }
            }

            // If no optimization panned out, just use the original default
            return item.DefaultSubtitleStreamIndex;
        }

        private static bool IsEfficientExternalSubtitleProfile(SubtitleProfile profile, MediaStream stream)
        {
            return profile.Method == SubtitleDeliveryMethod.External
                && (IsVobSubMksProfile(profile, stream)
                    || (!IsVobSubMksDeliveryProfile(profile) && string.Equals(profile.Format, stream.Codec, StringComparison.OrdinalIgnoreCase)));
        }

        /// <summary>
        /// Normalizes input container.
        /// </summary>
        /// <param name="mediaSource">The <see cref="MediaSourceInfo"/>.</param>
        /// <param name="subtitleStream">The <see cref="MediaStream"/> of the subtitle stream.</param>
        /// <param name="subtitleProfiles">The list of supported <see cref="SubtitleProfile"/>s.</param>
        /// <param name="playMethod">The <see cref="PlayMethod"/>.</param>
        /// <param name="transcoderSupport">The <see cref="ITranscoderSupport"/>.</param>
        /// <param name="outputContainer">The output container.</param>
        /// <param name="transcodingSubProtocol">The subtitle transcoding protocol.</param>
        /// <returns>The normalized input container.</returns>
        public static SubtitleProfile GetSubtitleProfile(
            MediaSourceInfo mediaSource,
            MediaStream subtitleStream,
            SubtitleProfile[] subtitleProfiles,
            PlayMethod playMethod,
            ITranscoderSupport transcoderSupport,
            string? outputContainer,
            MediaStreamProtocol? transcodingSubProtocol)
        {
            if (CanConsiderEmbedSubtitle(subtitleStream, playMethod, transcodingSubProtocol, outputContainer))
            {
                // Look for supported embedded subs of the same format
                foreach (var profile in subtitleProfiles)
                {
                    if (!profile.SupportsLanguage(subtitleStream.Language))
                    {
                        continue;
                    }

                    if (profile.Method != SubtitleDeliveryMethod.Embed)
                    {
                        continue;
                    }

                    if (!ContainerHelper.ContainsContainer(profile.Container, outputContainer))
                    {
                        continue;
                    }

                    if (IsEmbedUnsupportedForTranscode(playMethod, outputContainer))
                    {
                        continue;
                    }

                    if (IsEmbeddedFormatMatch(subtitleStream, profile))
                    {
                        return profile;
                    }
                }

                // Look for supported embedded subs of a convertible format
                foreach (var profile in subtitleProfiles)
                {
                    if (!profile.SupportsLanguage(subtitleStream.Language))
                    {
                        continue;
                    }

                    if (profile.Method != SubtitleDeliveryMethod.Embed)
                    {
                        continue;
                    }

                    if (!ContainerHelper.ContainsContainer(profile.Container, outputContainer))
                    {
                        continue;
                    }

                    if (IsEmbedUnsupportedForTranscode(playMethod, outputContainer))
                    {
                        continue;
                    }

                    if (IsConvertibleTextSubtitle(subtitleStream, profile))
                    {
                        return profile;
                    }
                }
            }

            // Look for an external or hls profile that matches the stream type (text/graphical) and doesn't require conversion
            return GetExternalSubtitleProfile(mediaSource, subtitleStream, subtitleProfiles, playMethod, transcoderSupport, false) ??
                GetExternalSubtitleProfile(mediaSource, subtitleStream, subtitleProfiles, playMethod, transcoderSupport, true) ??
                new SubtitleProfile
                {
                    Method = SubtitleDeliveryMethod.Encode,
                    Format = subtitleStream.Codec
                };
        }

        private static bool IsEmbeddedFormatMatch(MediaStream subtitleStream, SubtitleProfile profile)
        {
            return subtitleStream.IsTextSubtitleStream == MediaStream.IsTextFormat(profile.Format) && string.Equals(profile.Format, subtitleStream.Codec, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsEmbedUnsupportedForTranscode(PlayMethod playMethod, string? outputContainer)
        {
            return playMethod == PlayMethod.Transcode && !IsSubtitleEmbedSupported(outputContainer);
        }

        private static bool IsConvertibleTextSubtitle(MediaStream subtitleStream, SubtitleProfile profile)
        {
            return subtitleStream.IsTextSubtitleStream && subtitleStream.SupportsSubtitleConversionTo(profile.Format);
        }

        private static bool IsSubtitleEmbedSupported(string? transcodingContainer)
        {
            if (!string.IsNullOrEmpty(transcodingContainer))
            {
                if (ContainerHelper.ContainsContainer(transcodingContainer, "ts,mpegts,mp4"))
                {
                    return false;
                }

                if (ContainerHelper.ContainsContainer(transcodingContainer, "mkv,matroska"))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool CanConsiderEmbedSubtitle(MediaStream subtitleStream, PlayMethod playMethod, MediaStreamProtocol? transcodingSubProtocol, string? outputContainer)
        {
            if (subtitleStream.IsExternal)
            {
                return playMethod == PlayMethod.Transcode
                    && transcodingSubProtocol != MediaStreamProtocol.hls
                    && IsSubtitleEmbedSupported(outputContainer);
            }

            return playMethod != PlayMethod.Transcode
                || transcodingSubProtocol != MediaStreamProtocol.hls;
        }

        private static SubtitleProfile? GetExternalSubtitleProfile(MediaSourceInfo mediaSource, MediaStream subtitleStream, SubtitleProfile[] subtitleProfiles, PlayMethod playMethod, ITranscoderSupport transcoderSupport, bool allowConversion)
        {
            foreach (var profile in subtitleProfiles)
            {
                if (IsNeitherExternalNorHls(profile))
                {
                    continue;
                }

                if (IsHlsWithoutTranscode(profile, playMethod))
                {
                    continue;
                }

                if (!profile.SupportsLanguage(subtitleStream.Language))
                {
                    continue;
                }

                if (IsUnextractableEmbeddedSubtitle(subtitleStream, playMethod, transcoderSupport))
                {
                    continue;
                }

                bool isVobSubMksProfile = IsVobSubMksProfile(profile, subtitleStream);

                if (IsMatchingExternalOrHlsProfile(profile, subtitleStream, isVobSubMksProfile))
                {
                    bool requiresConversion = DoesRequireConversion(isVobSubMksProfile, subtitleStream, profile);

                    if (!requiresConversion)
                    {
                        return profile;
                    }

                    if (!allowConversion)
                    {
                        continue;
                    }

                    // TODO: Build this into subtitleStream.SupportsExternalStream
                    if (mediaSource.IsInfiniteStream)
                    {
                        continue;
                    }

                    if (CanConvertToExternalTextSubtitle(subtitleStream, profile))
                    {
                        return profile;
                    }
                }
            }

            return null;
        }

        private static bool IsNeitherExternalNorHls(SubtitleProfile profile)
        {
            return profile.Method != SubtitleDeliveryMethod.External && profile.Method != SubtitleDeliveryMethod.Hls;
        }

        private static bool IsHlsWithoutTranscode(SubtitleProfile profile, PlayMethod playMethod)
        {
            return profile.Method == SubtitleDeliveryMethod.Hls && playMethod != PlayMethod.Transcode;
        }

        private static bool DoesRequireConversion(bool isVobSubMksProfile, MediaStream subtitleStream, SubtitleProfile profile)
        {
            return !isVobSubMksProfile
                && !string.Equals(subtitleStream.Codec, profile.Format, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsUnextractableEmbeddedSubtitle(MediaStream subtitleStream, PlayMethod playMethod, ITranscoderSupport transcoderSupport)
        {
            return !subtitleStream.IsExternal
                && playMethod == PlayMethod.Transcode
                && !transcoderSupport.CanExtractSubtitles(subtitleStream.Codec)
                && !subtitleStream.IsPgsSubtitleStream
                && !subtitleStream.IsVobSubSubtitleStream;
        }

        private static bool IsMatchingExternalOrHlsProfile(SubtitleProfile profile, MediaStream subtitleStream, bool isVobSubMksProfile)
        {
            return (profile.Method == SubtitleDeliveryMethod.External
                    && (isVobSubMksProfile
                        || (!IsVobSubMksDeliveryProfile(profile) && subtitleStream.IsTextSubtitleStream == MediaStream.IsTextFormat(profile.Format)))) ||
                (profile.Method == SubtitleDeliveryMethod.Hls && subtitleStream.IsTextSubtitleStream);
        }

        private static bool CanConvertToExternalTextSubtitle(MediaStream subtitleStream, SubtitleProfile profile)
        {
            return subtitleStream.IsTextSubtitleStream && subtitleStream.SupportsExternalStream && subtitleStream.SupportsSubtitleConversionTo(profile.Format);
        }

        private static bool IsVobSubMksDeliveryProfile(SubtitleProfile profile)
        {
            return MediaStream.IsVobSubFormat(profile.Format)
                && !string.IsNullOrWhiteSpace(profile.Container)
                && ContainerHelper.ContainsContainer(profile.Container, "mks");
        }

        private static bool IsVobSubMksProfile(SubtitleProfile profile, MediaStream subtitleStream)
        {
            // FFmpeg cannot mux VobSub back into an .idx/.sub pair, so extracted VobSub streams are exposed as .mks.
            return IsVobSubMksDeliveryProfile(profile)
                && subtitleStream.IsVobSubSubtitleStream
                && (!subtitleStream.IsExternal || subtitleStream.Path?.EndsWith(".mks", StringComparison.OrdinalIgnoreCase) == true);
        }
    }
}
