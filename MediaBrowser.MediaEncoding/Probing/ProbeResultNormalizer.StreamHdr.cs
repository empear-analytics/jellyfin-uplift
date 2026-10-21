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
        private static void SetColorInfo(MediaStream stream, MediaStreamInfo streamInfo)
        {
            if (!string.IsNullOrEmpty(streamInfo.ColorRange))
            {
                stream.ColorRange = streamInfo.ColorRange;
            }

            if (!string.IsNullOrEmpty(streamInfo.ColorSpace))
            {
                stream.ColorSpace = streamInfo.ColorSpace;
            }

            if (!string.IsNullOrEmpty(streamInfo.ColorTransfer))
            {
                stream.ColorTransfer = streamInfo.ColorTransfer;
            }

            if (!string.IsNullOrEmpty(streamInfo.ColorPrimaries))
            {
                stream.ColorPrimaries = streamInfo.ColorPrimaries;
            }
        }

        private static void ApplySideData(MediaStream stream, MediaStreamInfo streamInfo)
        {
            if (streamInfo.SideDataList is not null)
            {
                foreach (var data in streamInfo.SideDataList)
                {
                    // Parse Dolby Vision metadata from side_data
                    if (string.Equals(data.SideDataType, "DOVI configuration record", StringComparison.OrdinalIgnoreCase))
                    {
                        stream.DvVersionMajor = data.DvVersionMajor;
                        stream.DvVersionMinor = data.DvVersionMinor;
                        stream.DvProfile = data.DvProfile;
                        stream.DvLevel = data.DvLevel;
                        stream.RpuPresentFlag = data.RpuPresentFlag;
                        stream.ElPresentFlag = data.ElPresentFlag;
                        stream.BlPresentFlag = data.BlPresentFlag;
                        stream.DvBlSignalCompatibilityId = data.DvBlSignalCompatibilityId;
                    }

                    // Parse video rotation metadata from side_data
                    else if (string.Equals(data.SideDataType, "Display Matrix", StringComparison.OrdinalIgnoreCase))
                    {
                        stream.Rotation = data.Rotation;
                    }

                    // Parse video frame cropping metadata from side_data
                    // TODO: save them and make HW filters to apply them in HWA pipelines
                    else if (string.Equals(data.SideDataType, "Frame Cropping", StringComparison.OrdinalIgnoreCase))
                    {
                        // Streams containing artificially added frame cropping
                        // metadata should not be marked as anamorphic.
                        stream.IsAnamorphic = false;
                    }
                }
            }
        }

        private static void SetHdr10PlusFlag(MediaStream stream, IReadOnlyList<MediaFrameInfo> frameInfoList)
        {
            var frameInfo = frameInfoList?.FirstOrDefault(i => i.StreamIndex == stream.Index);
            if (frameInfo?.SideDataList is not null
                && frameInfo.SideDataList.Any(data => string.Equals(data.SideDataType, "HDR Dynamic Metadata SMPTE2094-40 (HDR10+)", StringComparison.OrdinalIgnoreCase)))
            {
                stream.Hdr10PlusPresentFlag = true;
            }
        }
    }
}
