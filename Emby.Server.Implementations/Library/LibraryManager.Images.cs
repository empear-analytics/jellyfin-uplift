#pragma warning disable CS1591
#pragma warning disable CA5394

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BitFaster.Caching.Lru;
using Emby.Naming.Common;
using Emby.Naming.TV;
using Emby.Naming.Video;
using Emby.Server.Implementations.Library.Resolvers;
using Emby.Server.Implementations.Playlists;
using Emby.Server.Implementations.ScheduledTasks.Tasks;
using Emby.Server.Implementations.Sorting;
using Jellyfin.Data;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Extensions;
using MediaBrowser.Common.Extensions;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Drawing;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.IO;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Resolvers;
using MediaBrowser.Controller.Sorting;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Drawing;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Library;
using MediaBrowser.Model.Querying;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;
using Episode = MediaBrowser.Controller.Entities.TV.Episode;
using EpisodeInfo = Emby.Naming.TV.EpisodeInfo;
using Genre = MediaBrowser.Controller.Entities.Genre;
using Person = MediaBrowser.Controller.Entities.Person;
using VideoResolver = Emby.Naming.Video.VideoResolver;

namespace Emby.Server.Implementations.Library
{
    public partial class LibraryManager
    {
        private static bool IsMissingImageMetadata(ItemImageInfo image)
        {
            return image.Width == 0 || image.Height == 0 || string.IsNullOrEmpty(image.BlurHash);
        }

        private bool ImageNeedsRefresh(ItemImageInfo image)
        {
            if (image.Path is not null && image.IsLocalFile)
            {
                if (IsMissingImageMetadata(image))
                {
                    return true;
                }

                try
                {
                    return image.DateModified.Subtract(_fileSystem.GetLastWriteTimeUtc(image.Path)).Duration().TotalSeconds > 1;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Cannot get file info for {0}", image.Path);
                    return false;
                }
            }

            return image.Path is not null && !image.IsLocalFile;
        }

        /// <inheritdoc />
        public async Task UpdateImagesAsync(BaseItem item, bool forceUpdate = false)
        {
            ArgumentNullException.ThrowIfNull(item);

            var outdated = forceUpdate
                ? item.ImageInfos.Where(i => i.Path is not null).ToArray()
                : item.ImageInfos.Where(ImageNeedsRefresh).ToArray();

            var parentItem = item.GetParent();
            var isLiveTvShow = IsLiveTvShow(item, parentItem);

            // Skip image processing if current or live tv show
            if (outdated.Length == 0 || isLiveTvShow)
            {
                RegisterItem(item);
                return;
            }

            foreach (var img in outdated)
            {
                var image = img;
                if (!img.IsLocalFile)
                {
                    try
                    {
                        var index = item.GetImageIndex(img);
                        image = await ConvertImageToLocal(item, img, index, true).ConfigureAwait(false);
                    }
                    catch (ArgumentException)
                    {
                        _logger.LogWarning("Cannot get image index for {ImagePath}", img.Path);
                        continue;
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or IOException)
                    {
                        _logger.LogWarning(ex, "Cannot fetch image from {ImagePath}", img.Path);
                        continue;
                    }
                    catch (HttpRequestException ex)
                    {
                        _logger.LogWarning(ex, "Cannot fetch image from {ImagePath}. Http status code: {HttpStatus}", img.Path, ex.StatusCode);
                        continue;
                    }
                }

                if (IsMissingImageFile(image))
                {
                    _logger.LogWarning(
                        "{ImageType} image for {ItemName} ({ItemId}) not found at \"{ImagePath}\", source was {SourcePath}",
                        img.Type,
                        item.Name,
                        item.Id,
                        image.Path,
                        img.Path);
                    continue;
                }

                ImageDimensions size;
                size = UpdateImageDimensions(item, image);

                UpdateImageBlurHash(image, size);

                UpdateImageDateModified(image);
            }

            item.ValidateImages();

            await _persistenceService.SaveImagesAsync(item).ConfigureAwait(false);

            RegisterItem(item);
        }

        private static bool IsLiveTvShow(BaseItem item, BaseItem parentItem)
        {
            return item.SourceType != SourceType.Library &&
                   parentItem is not null &&
                   parentItem.SourceType != SourceType.Library; // not a channel
        }

        private static bool IsMissingImageFile(ItemImageInfo image)
        {
            return string.IsNullOrEmpty(image.Path) || !File.Exists(image.Path);
        }

        private ImageDimensions UpdateImageDimensions(BaseItem item, ItemImageInfo image)
        {
            ImageDimensions size;
            try
            {
                size = _imageProcessor.GetImageDimensions(item, image);
                image.Width = size.Width;
                image.Height = size.Height;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Cannot get image dimensions for {ImagePath}", image.Path);
                size = default;
                image.Width = 0;
                image.Height = 0;
            }

            return size;
        }

        private void UpdateImageBlurHash(ItemImageInfo image, ImageDimensions size)
        {
            try
            {
                var blurhash = _imageProcessor.GetImageBlurHash(image.Path, size);
                image.BlurHash = blurhash;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Cannot compute blurhash for {ImagePath}", image.Path);
                image.BlurHash = string.Empty;
            }
        }

        private void UpdateImageDateModified(ItemImageInfo image)
        {
            try
            {
                var modifiedDate = _fileSystem.GetLastWriteTimeUtc(image.Path);
                image.DateModified = modifiedDate;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Cannot update DateModified for {ImagePath}", image.Path);
            }
        }

        public async Task<ItemImageInfo> ConvertImageToLocal(BaseItem item, ItemImageInfo image, int imageIndex, bool removeOnFailure)
        {
            foreach (var url in image.Path.Split('|'))
            {
                try
                {
                    _logger.LogDebug("ConvertImageToLocal item {0} - image url: {1}", item.Id, url);

                    await ProviderManager.SaveImage(item, url, image.Type, imageIndex, CancellationToken.None).ConfigureAwait(false);

                    await item.UpdateToRepositoryAsync(ItemUpdateType.ImageUpdate, CancellationToken.None).ConfigureAwait(false);

                    var localImage = item.GetImageInfo(image.Type, imageIndex);
                    if (localImage is null)
                    {
                        throw new InvalidOperationException(string.Format(
                            CultureInfo.InvariantCulture,
                            "Downloaded {0} image {1} from {2} is not attached to {3} ({4})",
                            image.Type,
                            imageIndex,
                            url,
                            item.Name,
                            item.Id));
                    }

                    return localImage;
                }
                catch (HttpRequestException ex)
                {
                    if (IsNotFoundOrForbidden(ex))
                    {
                        _logger.LogDebug(ex, "Error downloading image {Url}", url);
                        continue;
                    }

                    throw;
                }
            }

            if (removeOnFailure)
            {
                // Remove this image to prevent it from retrying over and over
                item.RemoveImage(image);
                await item.UpdateToRepositoryAsync(ItemUpdateType.ImageUpdate, CancellationToken.None).ConfigureAwait(false);
            }

            throw new InvalidOperationException(string.Format(
                CultureInfo.InvariantCulture,
                "Unable to convert any {0} image url in \"{1}\" to a local file for {2} ({3})",
                image.Type,
                image.Path,
                item.Name,
                item.Id));
        }

        private static bool IsNotFoundOrForbidden(HttpRequestException ex)
        {
            return ex.StatusCode.HasValue
                && (ex.StatusCode.Value == HttpStatusCode.NotFound || ex.StatusCode.Value == HttpStatusCode.Forbidden);
        }
    }
}
