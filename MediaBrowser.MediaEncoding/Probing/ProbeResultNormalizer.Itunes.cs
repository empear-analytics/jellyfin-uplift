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
        private void FetchFromItunesInfo(string xml, MediaInfo info)
        {
            // Make things simpler and strip out the dtd
            var plistIndex = xml.IndexOf("<plist", StringComparison.OrdinalIgnoreCase);

            if (plistIndex != -1)
            {
                xml = xml.Substring(plistIndex);
            }

            xml = "<?xml version=\"1.0\"?>" + xml;

            // <?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n<plist version=\"1.0\">\n<dict>\n\t<key>cast</key>\n\t<array>\n\t\t<dict>\n\t\t\t<key>name</key>\n\t\t\t<string>Blender Foundation</string>\n\t\t</dict>\n\t\t<dict>\n\t\t\t<key>name</key>\n\t\t\t<string>Janus Bager Kristensen</string>\n\t\t</dict>\n\t</array>\n\t<key>directors</key>\n\t<array>\n\t\t<dict>\n\t\t\t<key>name</key>\n\t\t\t<string>Sacha Goedegebure</string>\n\t\t</dict>\n\t</array>\n\t<key>studio</key>\n\t<string>Blender Foundation</string>\n</dict>\n</plist>\n
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml)))
            using (var streamReader = new StreamReader(stream))
            {
                try
                {
                    using (var reader = XmlReader.Create(streamReader))
                    {
                        reader.MoveToContent();
                        reader.Read();

                        // Loop through each element
                        while (!reader.EOF && reader.ReadState == ReadState.Interactive)
                        {
                            if (reader.NodeType == XmlNodeType.Element)
                            {
                                switch (reader.Name)
                                {
                                    case "dict":
                                        if (reader.IsEmptyElement)
                                        {
                                            reader.Read();
                                            continue;
                                        }

                                        using (var subtree = reader.ReadSubtree())
                                        {
                                            ReadFromDictNode(subtree, info);
                                        }

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
                    }
                }
                catch (XmlException)
                {
                    // I've seen probe examples where the iTunMOVI value is just "<"
                    // So we should not allow this to fail the entire probing operation
                }
            }
        }

        private void ReadFromDictNode(XmlReader reader, MediaInfo info)
        {
            string currentKey = null;
            var pairs = new List<NameValuePair>();

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
                            if (!string.IsNullOrWhiteSpace(currentKey))
                            {
                                ProcessPairs(currentKey, pairs, info);
                            }

                            currentKey = reader.ReadElementContentAsString();
                            pairs = new List<NameValuePair>();
                            break;
                        case "string":
                            AddStringValuePair(reader, pairs);

                            break;
                        case "array":
                            if (reader.IsEmptyElement)
                            {
                                reader.Read();
                                continue;
                            }

                            using (var subtree = reader.ReadSubtree())
                            {
                                if (!string.IsNullOrWhiteSpace(currentKey))
                                {
                                    pairs.AddRange(ReadValueArray(subtree));
                                }
                            }

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
        }

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

        private List<NameValuePair> ReadValueArray(XmlReader reader)
        {
            var pairs = new List<NameValuePair>();

            reader.MoveToContent();
            reader.Read();

            // Loop through each element
            while (!reader.EOF && reader.ReadState == ReadState.Interactive)
            {
                if (reader.NodeType == XmlNodeType.Element)
                {
                    switch (reader.Name)
                    {
                        case "dict":

                            if (reader.IsEmptyElement)
                            {
                                reader.Read();
                                continue;
                            }

                            using (var subtree = reader.ReadSubtree())
                            {
                                var dict = GetNameValuePair(subtree);
                                if (dict is not null)
                                {
                                    pairs.Add(dict);
                                }
                            }

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

            return pairs;
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
