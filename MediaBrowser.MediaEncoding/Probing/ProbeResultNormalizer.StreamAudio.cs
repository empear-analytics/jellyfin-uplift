#nullable disable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Jellyfin.Data.Enums;
using Jellyfin.Extensions;
using MediaBrowser.Controller.Extensions;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.MediaInfo;
using Microsoft.Extensions.Logging;

namespace MediaBrowser.MediaEncoding.Probing
{
    public partial class ProbeResultNormalizer
    {
        private void SetAudioStreamInfo(MediaStream stream, MediaStreamInfo streamInfo)
        {
            stream.Type = MediaStreamType.Audio;
            stream.LocalizedDefault = _localization.GetLocalizedString("Default");
            stream.LocalizedExternal = _localization.GetLocalizedString("External");
            stream.LocalizedOriginal = _localization.GetLocalizedString("Original");
            if (!string.IsNullOrEmpty(stream.Language))
            {
                stream.LocalizedLanguage = _localization.GetLanguageDisplayName(stream.Language);
            }

            stream.Channels = streamInfo.Channels;

            if (int.TryParse(streamInfo.SampleRate, CultureInfo.InvariantCulture, out var sampleRate))
            {
                stream.SampleRate = sampleRate;
            }

            stream.ChannelLayout = ParseChannelLayout(streamInfo.ChannelLayout);

            if (streamInfo.BitsPerSample > 0)
            {
                stream.BitDepth = streamInfo.BitsPerSample;
            }
            else if (streamInfo.BitsPerRawSample > 0)
            {
                stream.BitDepth = streamInfo.BitsPerRawSample;
            }

            SetAudioTitleFromTags(stream, streamInfo);
        }

        private static void SetAudioTitleFromTags(MediaStream stream, MediaStreamInfo streamInfo)
        {
            if (string.IsNullOrEmpty(stream.Title))
            {
                // FFprobe exposes MP4 track names via the name tag rather than title
                stream.Title = GetDictionaryValue(streamInfo.Tags, "name");

                if (string.IsNullOrEmpty(stream.Title))
                {
                    // fall back to handler_name if populated and not the default "SoundHandler"
                    string handlerName = GetDictionaryValue(streamInfo.Tags, "handler_name");
                    if (!string.IsNullOrEmpty(handlerName) && !string.Equals(handlerName, "SoundHandler", StringComparison.OrdinalIgnoreCase))
                    {
                        stream.Title = handlerName;
                    }
                }
            }
        }

        private void SetSubtitleStreamInfo(MediaStream stream, MediaStreamInfo streamInfo)
        {
            stream.Type = MediaStreamType.Subtitle;
            stream.Codec = NormalizeSubtitleCodec(stream.Codec);
            stream.LocalizedUndefined = _localization.GetLocalizedString("Undefined");
            stream.LocalizedDefault = _localization.GetLocalizedString("Default");
            stream.LocalizedForced = _localization.GetLocalizedString("Forced");
            stream.LocalizedExternal = _localization.GetLocalizedString("External");
            stream.LocalizedHearingImpaired = _localization.GetLocalizedString("HearingImpaired");
            if (!string.IsNullOrEmpty(stream.Language))
            {
                stream.LocalizedLanguage = _localization.GetLanguageDisplayName(stream.Language);
            }

            if (string.IsNullOrEmpty(stream.Title))
            {
                // FFprobe exposes MP4 track names via the name tag rather than title
                stream.Title = GetDictionaryValue(streamInfo.Tags, "name");

                if (string.IsNullOrEmpty(stream.Title))
                {
                    // fall back to handler_name if populated and not the default "SubtitleHandler"
                    string handlerName = GetDictionaryValue(streamInfo.Tags, "handler_name");
                    if (!string.IsNullOrEmpty(handlerName) && !string.Equals(handlerName, "SubtitleHandler", StringComparison.OrdinalIgnoreCase))
                    {
                        stream.Title = handlerName;
                    }
                }
            }
        }

        private static string ParseChannelLayout(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return null;
            }

            return input.AsSpan().LeftPart('(').ToString();
        }
    }
}
