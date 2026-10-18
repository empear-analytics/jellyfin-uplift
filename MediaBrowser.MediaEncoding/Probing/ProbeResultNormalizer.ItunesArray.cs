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
        private List<NameValuePair> ReadValueArray(XmlReader reader)
        {
            var pairs = new List<NameValuePair>();

            reader.MoveToContent();
            reader.Read();

            // Loop through each element
            while (!reader.EOF && reader.ReadState == ReadState.Interactive)
            {
                if (ItunesArrayReadEntry(reader, pairs))
                {
                    continue;
                }
            }

            return pairs;
        }

        private static bool ItunesArrayReadEntry(XmlReader reader, List<NameValuePair> pairs)
        {
            if (reader.NodeType == XmlNodeType.Element)
            {
                switch (reader.Name)
                {
                    case "dict":

                        if (reader.IsEmptyElement)
                        {
                            reader.Read();
                            return true;
                        }

                        AddDictPair(reader, pairs);

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

            return false;
        }

        private static void AddDictPair(XmlReader reader, List<NameValuePair> pairs)
        {
            using (var subtree = reader.ReadSubtree())
            {
                var dict = GetNameValuePair(subtree);
                if (dict is not null)
                {
                    pairs.Add(dict);
                }
            }
        }
    }
}
