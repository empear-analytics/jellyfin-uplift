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
        private static void AddStringValuePair(XmlReader reader, List<NameValuePair> pairs)
        {
            var value = reader.ReadElementContentAsString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                pairs.Add(new NameValuePair
                {
                    Name = value,
                    Value = value
                });
            }
        }

        private static void ProcessPairs(string key, List<NameValuePair> pairs, MediaInfo info)
        {
            List<BaseItemPerson> peoples = new List<BaseItemPerson>();
            var distinctPairs = pairs.Select(p => p.Value)
                    .Where(i => !string.IsNullOrWhiteSpace(i))
                    .Trimmed()
                    .Distinct(StringComparer.OrdinalIgnoreCase);

            if (string.Equals(key, "studio", StringComparison.OrdinalIgnoreCase))
            {
                info.Studios = distinctPairs.ToArray();
            }
            else if (string.Equals(key, "screenwriters", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var pair in distinctPairs)
                {
                    peoples.Add(new BaseItemPerson
                    {
                        Name = pair,
                        Type = PersonKind.Writer
                    });
                }
            }
            else if (string.Equals(key, "producers", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var pair in distinctPairs)
                {
                    peoples.Add(new BaseItemPerson
                    {
                        Name = pair,
                        Type = PersonKind.Producer
                    });
                }
            }
            else if (string.Equals(key, "directors", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var pair in distinctPairs)
                {
                    peoples.Add(new BaseItemPerson
                    {
                        Name = pair,
                        Type = PersonKind.Director
                    });
                }
            }

            info.People = peoples.ToArray();
        }

        private static NameValuePair GetNameValuePair(XmlReader reader)
        {
            string name = null;
            string value = null;

            reader.MoveToContent();
            reader.Read();

            // Loop through each element
            while (!reader.EOF && reader.ReadState == ReadState.Interactive)
            {
                if (reader.NodeType == XmlNodeType.Element)
                {
                    switch (reader.Name)
                    {
                        case "key":
                            name = reader.ReadNormalizedString();
                            break;
                        case "string":
                            value = reader.ReadNormalizedString();
                            break;
                        default:
                            reader.Skip();
                            break;
                    }
                }
                else
                {
                    reader.Read();
                }
            }

            if (string.IsNullOrEmpty(name)
                || string.IsNullOrEmpty(value))
            {
                return null;
            }

            return new NameValuePair
            {
                Name = name,
                Value = value
            };
        }
    }
}
