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
        private void AddSplitPeople(List<BaseItemPerson> people, string names, PersonKind kind)
        {
            foreach (var person in Split(names, false))
            {
                people.Add(new BaseItemPerson { Name = person, Type = kind });
            }
        }

        private void AddMusicalCredits(List<BaseItemPerson> people, Dictionary<string, string> tags)
        {
            if (tags.TryGetValue("composer", out var composer) && !string.IsNullOrWhiteSpace(composer))
            {
                AddSplitPeople(people, composer, PersonKind.Composer);
            }

            if (tags.TryGetValue("conductor", out var conductor) && !string.IsNullOrWhiteSpace(conductor))
            {
                AddSplitPeople(people, conductor, PersonKind.Conductor);
            }

            if (tags.TryGetValue("lyricist", out var lyricist) && !string.IsNullOrWhiteSpace(lyricist))
            {
                AddSplitPeople(people, lyricist, PersonKind.Lyricist);
            }
        }

        private void AddMixingCredits(List<BaseItemPerson> people, Dictionary<string, string> tags)
        {
            if (tags.TryGetValue("mixer", out var mixer) && !string.IsNullOrWhiteSpace(mixer))
            {
                AddSplitPeople(people, mixer, PersonKind.Mixer);
            }

            if (tags.TryGetValue("remixer", out var remixer) && !string.IsNullOrWhiteSpace(remixer))
            {
                AddSplitPeople(people, remixer, PersonKind.Remixer);
            }
        }

        private void AddWritingAndEngineeringCredits(List<BaseItemPerson> people, Dictionary<string, string> tags)
        {
            // In cases where there isn't sufficient information as to which role a writer performed on a recording, tagging software uses the "writer" tag.
            if (tags.TryGetValue("writer", out var writer) && !string.IsNullOrWhiteSpace(writer))
            {
                AddSplitPeople(people, writer, PersonKind.Writer);
            }

            if (tags.TryGetValue("arranger", out var arranger) && !string.IsNullOrWhiteSpace(arranger))
            {
                AddSplitPeople(people, arranger, PersonKind.Arranger);
            }

            if (tags.TryGetValue("engineer", out var engineer) && !string.IsNullOrWhiteSpace(engineer))
            {
                AddSplitPeople(people, engineer, PersonKind.Engineer);
            }
        }

        private void AddPerformers(List<BaseItemPerson> people, Dictionary<string, string> tags)
        {
            if (tags.TryGetValue("performer", out var performer) && !string.IsNullOrWhiteSpace(performer))
            {
                foreach (var person in Split(performer, false))
                {
                    Match match = PerformerRegex().Match(person);

                    // If the performer doesn't have any instrument/role associated, it won't match. In that case, chances are it's simply a band name, so we skip it.
                    if (match.Success)
                    {
                        people.Add(new BaseItemPerson
                        {
                            Name = match.Groups["name"].Value,
                            Type = PersonKind.Actor,
                            Role = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(match.Groups["instrument"].Value)
                        });
                    }
                }
            }
        }
    }
}
