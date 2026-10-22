#nullable disable

#pragma warning disable CS1591

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Jellyfin.Data;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Extensions;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Extensions;
using MediaBrowser.Controller.IO;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Dlna;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaInfo;
using MediaBrowser.Model.Session;
using Microsoft.Extensions.Configuration;
using IConfigurationManager = MediaBrowser.Common.Configuration.IConfigurationManager;

namespace MediaBrowser.Controller.MediaEncoding
{
    public partial class EncodingHelper
    {
        private static void AddStereoDownMixFilters(EncodingJobInfo state, EncodingOptions encodingOptions, List<string> filters)
        {
            var hasDownMixFilter = DownMixAlgorithmsHelper.AlgorithmFilterStrings.TryGetValue((encodingOptions.DownMixStereoAlgorithm, DownMixAlgorithmsHelper.InferChannelLayout(state.AudioStream)), out var downMixFilterString);
            if (hasDownMixFilter)
            {
                filters.Add(downMixFilterString);
            }

            if (!encodingOptions.DownMixAudioBoost.Equals(1))
            {
                filters.Add("volume=" + encodingOptions.DownMixAudioBoost.ToString(CultureInfo.InvariantCulture));
            }
        }

        private static bool NeedsSubtitleAudioTimestampShift(EncodingJobInfo state, bool isCopyingTimestamps)
        {
            return state.SubtitleStream is not null && state.SubtitleStream.IsTextSubtitleStream && ShouldEncodeSubtitle(state) && !isCopyingTimestamps;
        }

        private static int? GetHlsCompatibleChannelCount(int? resultChannels)
        {
            // We can let FFMpeg supply an extra LFE channel for 5ch and 7ch to make them 5.1 and 7.1
            if (resultChannels == 5)
            {
                resultChannels = 6;
            }
            else if (resultChannels == 7)
            {
                resultChannels = 8;
            }
            else
            {
                // For other weird layout, just downmix to stereo for compatibility
                resultChannels = 2;
            }

            return resultChannels;
        }

        private static bool HasUnsupportedHlsChannelLayout(EncodingJobInfo state, int? resultChannels)
        {
            return state.TranscodingType != TranscodingJobType.Progressive
                && ((resultChannels > 2 && resultChannels < 6) || resultChannels == 7);
        }

        private int? GetAudioTranscodeChannels(EncodingJobInfo state, BaseEncodingJobOptions request, string codec, int? resultChannels)
        {
            var isTranscodingAudio = !IsCopyCodec(codec);

            if (isTranscodingAudio)
            {
                var audioEncoder = GetAudioEncoder(state);
                if (!_audioTranscodeChannelLookup.TryGetValue(audioEncoder, out var transcoderChannelLimit))
                {
                    // Set default max transcoding channels to 8 to prevent encoding errors due to asking for too many channels.
                    transcoderChannelLimit = 8;
                }

                // Set resultChannels to minimum between resultChannels, TranscodingMaxAudioChannels, transcoderChannelLimit
                resultChannels = transcoderChannelLimit < resultChannels ? transcoderChannelLimit : resultChannels ?? transcoderChannelLimit;

                if (request.TranscodingMaxAudioChannels < resultChannels)
                {
                    resultChannels = request.TranscodingMaxAudioChannels;
                }

                // Avoid transcoding to audio channels other than 1ch, 2ch, 6ch (5.1 layout) and 8ch (7.1 layout).
                // https://developer.apple.com/documentation/http_live_streaming/hls_authoring_specification_for_apple_devices
                if (HasUnsupportedHlsChannelLayout(state, resultChannels))
                {
                    resultChannels = GetHlsCompatibleChannelCount(resultChannels);
                }
            }

            return resultChannels;
        }
    }
}
