#nullable disable

#pragma warning disable CS1591

using System;
using System.Collections.Generic;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Model.Entities;

namespace Emby.Server.Implementations.Library
{
    public partial class MediaSourceManager
    {
        public IReadOnlyList<MediaStream> GetMediaStreams(MediaStreamQuery query)
        {
            var list = _mediaStreamRepository.GetMediaStreams(query);

            foreach (var stream in list)
            {
                stream.SupportsExternalStream = StreamSupportsExternalStream(stream);
            }

            return list;
        }

        private static bool StreamSupportsExternalStream(MediaStream stream)
        {
            if (stream.IsExternal)
            {
                return true;
            }

            if (stream.IsTextSubtitleStream)
            {
                return true;
            }

            if (stream.IsPgsSubtitleStream)
            {
                return true;
            }

            if (stream.IsVobSubSubtitleStream)
            {
                return true;
            }

            return false;
        }

        public IReadOnlyList<MediaStream> GetMediaStreams(Guid itemId)
        {
            var list = GetMediaStreams(new MediaStreamQuery
            {
                ItemId = itemId
            });

            return GetMediaStreamsForItem(list);
        }

        private IReadOnlyList<MediaStream> GetMediaStreamsForItem(IReadOnlyList<MediaStream> streams)
        {
            foreach (var stream in streams)
            {
                if (stream.Type == MediaStreamType.Subtitle)
                {
                    stream.SupportsExternalStream = StreamSupportsExternalStream(stream);
                }
            }

            return streams;
        }

        /// <inheritdoc />
        public IReadOnlyList<MediaAttachment> GetMediaAttachments(MediaAttachmentQuery query)
        {
            return _mediaAttachmentRepository.GetMediaAttachments(query);
        }

        /// <inheritdoc />
        public IReadOnlyList<MediaAttachment> GetMediaAttachments(Guid itemId)
        {
            return GetMediaAttachments(new MediaAttachmentQuery
            {
                ItemId = itemId
            });
        }
    }
}
