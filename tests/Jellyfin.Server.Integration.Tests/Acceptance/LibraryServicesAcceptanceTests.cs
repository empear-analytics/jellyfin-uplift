using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.IO;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Library;
using MediaBrowser.Model.Querying;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

using AudioItem = MediaBrowser.Controller.Entities.Audio.Audio;
using KeyframeData = Jellyfin.MediaEncoding.Keyframes.KeyframeData;

namespace Jellyfin.Server.Integration.Tests.Acceptance;

/// <summary>
/// Acceptance tests for the library services around playback: user views, latest items, instant
/// mixes, user data, extracted-data paths, keyframes and their deletion.
/// </summary>
/// <remarks>
/// These protect the refactoring campaign (docs/refactoring/). They run against the real in-memory
/// server and call the services through their plugin-facing interfaces, without mocks. The
/// libraries are created in a temp folder, so their ids depend on the machine: outputs record
/// names, not ids, and mask folders. Set JELLYFIN_ACCEPTANCE_RECORD=1 to (re)record.
/// </remarks>
public sealed class LibraryServicesAcceptanceTests : IClassFixture<AcceptanceApplicationFactory>
{
    private static readonly DateTime _epoch = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly AcceptanceApplicationFactory _factory;
    // xUnit creates a test class instance per test, so the libraries are kept per server.
    private static readonly SemaphoreSlim _setupLock = new(1, 1);
    private static readonly Dictionary<AcceptanceApplicationFactory, Library> _libraries = [];

    public LibraryServicesAcceptanceTests(AcceptanceApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task UserViews_MatchRecording()
    {
        var library = await GetLibraryAsync();
        var views = Service<IUserViewManager>();
        var users = Service<IUserManager>();
        var output = new StringBuilder();

        var preferenceVariants = new (string Name, Action<User> Apply)[]
        {
            ("defaults", _ => { }),
            ("ordered-reverse", u => u.SetPreference(PreferenceKind.OrderedViews, library.Folders.Values.Reverse().Select(f => f.Id.ToString("N")).ToArray())),
            ("exclude-music", u => u.SetPreference(PreferenceKind.MyMediaExcludes, [library.Folders["music"].Id.ToString("N")])),
            ("grouped-movies", u => u.SetPreference(PreferenceKind.GroupedFolders, [library.Folders["movies"].Id.ToString("N")])),
        };
        foreach (var (name, apply) in preferenceVariants)
        {
            var user = users.GetUsers().First();
            foreach (var kind in new[] { PreferenceKind.OrderedViews, PreferenceKind.MyMediaExcludes, PreferenceKind.GroupedFolders })
            {
                user.SetPreference(kind, Array.Empty<string>());
            }

            apply(user);
            await users.UpdateUserAsync(user);

            foreach (var (external, hidden) in new[] { (false, false), (true, false), (true, true) })
            {
                foreach (var presets in new[] { Array.Empty<CollectionType?>(), new CollectionType?[] { CollectionType.movies, CollectionType.tvshows } })
                {
                    var result = views.GetUserViews(new UserViewQuery
                    {
                        User = users.GetUsers().First(),
                        IncludeExternalContent = external,
                        IncludeHidden = hidden,
                        PresetViews = presets
                    });
                    output.Append(CultureInfo.InvariantCulture, $"{name} external={external} hidden={hidden} presets={presets.Length} -> ")
                        .Append(string.Join(", ", result.Select(Describe)))
                        .Append('\n');
                }
            }
        }

        RecordedOutput.Verify("LibraryServices", "user-views", output.ToString().TrimEnd());
    }

    [Fact]
    public async Task LatestItems_MatchRecording()
    {
        var library = await GetLibraryAsync();
        var views = Service<IUserViewManager>();
        var user = Service<IUserManager>().GetUsers().First();
        var output = new StringBuilder();

        var parents = new (string Name, Guid Id)[] { ("all", Guid.Empty), ("movies", library.Folders["movies"].Id), ("tvshows", library.Folders["tvshows"].Id), ("music", library.Folders["music"].Id) };
        foreach (var (parentName, parentId) in parents)
        {
            foreach (var group in new[] { true, false })
            {
                foreach (var played in new bool?[] { null, false })
                {
                    foreach (var types in new[] { Array.Empty<BaseItemKind>(), new[] { BaseItemKind.Movie }, new[] { BaseItemKind.Episode, BaseItemKind.Audio } })
                    {
                        var result = views.GetLatestItems(
                            new LatestItemsQuery { User = user, ParentId = parentId, GroupItems = group, IsPlayed = played, Limit = 4, IncludeItemTypes = types },
                            new DtoOptions(true));
                        output.Append(CultureInfo.InvariantCulture, $"parent={parentName} group={group} played={played?.ToString() ?? "-"} types={string.Join('|', types)} -> ")
                            .Append(string.Join("; ", result.Select(r => Describe(r.Item1) + "[" + string.Join(",", r.Item2.Select(c => c.Name)) + "]")))
                            .Append('\n');
                    }
                }
            }
        }

        RecordedOutput.Verify("LibraryServices", "latest-items", output.ToString().TrimEnd());
    }

    [Fact]
    public async Task InstantMix_MatchesRecording()
    {
        var library = await GetLibraryAsync();
        var music = Service<IMusicManager>();
        var user = Service<IUserManager>().GetUsers().First();
        var options = new DtoOptions(true);

        // Instant mixes are ordered randomly, so the recording lists them sorted.
        string Mix(IReadOnlyList<BaseItem> items) => items.Count + ": " + string.Join(", ", items.Select(i => i.Name).Order(StringComparer.Ordinal));
        var lines = new[]
        {
            "song: " + Mix(music.GetInstantMixFromItem(library.Tracks[0], user, options)),
            "album: " + Mix(music.GetInstantMixFromItem(library.Album, user, options)),
            "artist: " + Mix(music.GetInstantMixFromItem(library.Artist, user, options)),
            "folder: " + Mix(music.GetInstantMixFromItem(library.Folders["music"], user, options)),
            "genres rock: " + Mix(music.GetInstantMixFromGenres(["Rock"], user, options)),
            "genres jazz+pop: " + Mix(music.GetInstantMixFromGenres(["Jazz", "Pop"], user, options)),
            "genres none: " + Mix(music.GetInstantMixFromGenres([], user, options)),
            "movie: " + Mix(music.GetInstantMixFromItem(library.Movies[0], user, options)),
            "no user: " + Mix(music.GetInstantMixFromGenres(["Rock"], null, options)),
        };

        RecordedOutput.Verify("LibraryServices", "instant-mix", string.Join('\n', lines));
    }

    [Fact]
    public async Task UserData_MatchesRecording()
    {
        var library = await GetLibraryAsync();
        var data = Service<IUserDataManager>();
        var user = Service<IUserManager>().GetUsers().First();
        var movie = library.Movies[1];
        var output = new StringBuilder();

        var updates = new (string Name, UpdateUserItemDataDto Dto)[]
        {
            ("favorite-rated", new UpdateUserItemDataDto { IsFavorite = true, Rating = 7.5, Likes = true }),
            ("position-half", new UpdateUserItemDataDto { PlaybackPositionTicks = movie.RunTimeTicks / 2, LastPlayedDate = _epoch }),
            ("played", new UpdateUserItemDataDto { Played = true, PlayCount = 3 }),
            ("unplayed", new UpdateUserItemDataDto { Played = false, PlaybackPositionTicks = 0 }),
        };
        foreach (var (name, dto) in updates)
        {
            data.SaveUserData(user, movie, dto, UserDataSaveReason.UpdateUserData);
            output.Append(name).Append(" -> ").Append(Describe(data.GetUserDataDto(movie, user))).Append('\n');
        }

        var runtime = movie.RunTimeTicks ?? 0;
        foreach (var position in new long?[] { null, 0, runtime / 50, runtime / 2, runtime * 19 / 20, runtime + 1 })
        {
            var state = data.GetUserData(user, movie)!;
            state.Played = false;
            var finished = data.UpdatePlayState(movie, state, position);
            data.SaveUserData(user, movie, state, UserDataSaveReason.PlaybackProgress, CancellationToken.None);
            output.Append(CultureInfo.InvariantCulture, $"playstate position={position?.ToString(CultureInfo.InvariantCulture) ?? "-"} -> finished={finished} ")
                .Append(Describe(data.GetUserDataDto(movie, user)))
                .Append('\n');
        }

        data.ResetPlaybackStreamSelections(user, movie);
        var selections = data.GetUserData(user, movie)!;
        selections.AudioStreamIndex = 2;
        selections.SubtitleStreamIndex = 3;
        data.SaveUserData(user, movie, selections, UserDataSaveReason.UpdateUserData, CancellationToken.None);
        output.Append(CultureInfo.InvariantCulture, $"streams saved -> audio={Show(data.GetUserData(user, movie)!.AudioStreamIndex)}\n");
        data.ResetPlaybackStreamSelections(user, movie);
        output.Append(CultureInfo.InvariantCulture, $"streams reset -> audio={Show(data.GetUserData(user, movie)!.AudioStreamIndex)} subtitle={Show(data.GetUserData(user, movie)!.SubtitleStreamIndex)}\n");
        var resume = data.GetResumeUserData(user, movie);
        output.Append(CultureInfo.InvariantCulture, $"resume -> {resume?.UserData.PlaybackPositionTicks.ToString(CultureInfo.InvariantCulture) ?? "-"}\n");

        var series = library.Series;
        data.SaveUserData(user, library.Episodes[0], new UpdateUserItemDataDto { Played = true }, UserDataSaveReason.TogglePlayed);
        output.Append("series -> ").Append(Describe(data.GetUserDataDto(series, user)));

        RecordedOutput.Verify("LibraryServices", "user-data", output.ToString().TrimEnd());
    }

    [Fact]
    public async Task PathsKeyframesAndDeletion_MatchRecording()
    {
        var library = await GetLibraryAsync();
        var paths = Service<IPathManager>();
        var keyframes = Service<IKeyframeManager>();
        var external = Service<IExternalDataManager>();
        var movie = library.Movies[2];
        var sourceId = movie.Id.ToString("N");
        var lines = new List<string>
        {
            "attachment folder: " + paths.GetAttachmentFolderPath(sourceId),
            "attachment: " + paths.GetAttachmentPath(sourceId, "font.ttf"),
            "subtitle folder: " + paths.GetSubtitleFolderPath(sourceId),
            "subtitle: " + paths.GetSubtitlePath(sourceId, 3, ".srt"),
            "subtitle no dot: " + paths.GetSubtitlePath(sourceId, 4, "ass"),
            "trickplay: " + paths.GetTrickplayDirectory(movie),
            "trickplay with media: " + paths.GetTrickplayDirectory(movie, true),
            "chapter folder: " + paths.GetChapterImageFolderPath(movie),
            "chapter: " + paths.GetChapterImagePath(movie, TimeSpan.FromMinutes(12).Ticks),
            "extracted: " + string.Join(" | ", paths.GetExtractedDataPaths(movie)),
            "null source: " + (paths.GetAttachmentFolderPath(string.Empty) ?? "-") + " / " + (paths.GetSubtitlePath(string.Empty, 1, "srt") ?? "-")
        };

        await keyframes.SaveKeyframeDataAsync(movie.Id, new KeyframeData(TimeSpan.FromMinutes(90).Ticks, [0, 20_000_000, 40_000_000, 70_000_000]), CancellationToken.None);
        lines.Add("keyframes: " + string.Join(" | ", keyframes.GetKeyframeData(movie.Id).Select(k => k.TotalDuration + ":" + string.Join(",", k.KeyframeTicks))));

        // Files in every extracted-data location, then delete the item's external data.
        var created = new List<string>();
        foreach (var path in paths.GetExtractedDataPaths(movie).Append(paths.GetChapterImagePath(movie, 0)).Append(paths.GetAttachmentPath(sourceId, "font.ttf")!))
        {
            var file = Path.HasExtension(path) ? path : Path.Combine(path, "data.bin");
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            await File.WriteAllTextAsync(file, "x", TestContext.Current.CancellationToken);
            created.Add(file);
        }

        await external.DeleteExternalItemDataAsync(movie, CancellationToken.None);
        lines.Add("after deletion: " + string.Join(" | ", created.Select(f => Path.GetFileName(f) + "=" + File.Exists(f))));
        lines.Add("keyframes after deletion: " + keyframes.GetKeyframeData(movie.Id).Count);

        RecordedOutput.Verify("LibraryServices", "paths-keyframes-deletion", _factory.Mask(string.Join('\n', lines)));
    }

    private static string Describe(BaseItem? item)
        => item switch
        {
            CollectionFolder c => $"{c.Name}({c.CollectionType?.ToString() ?? "-"})",
            UserView v => $"{v.Name}<view:{v.ViewType?.ToString() ?? "-"}>",
            null => "-",
            _ => $"{item.Name}<{item.GetType().Name}>"
        };

    private static string Describe(UserItemDataDto? dto)
        => dto is null
            ? "-"
            : FormattableString.Invariant($"played={dto.Played} count={dto.PlayCount} fav={dto.IsFavorite} likes={dto.Likes?.ToString() ?? "-"} rating={dto.Rating?.ToString(CultureInfo.InvariantCulture) ?? "-"} pos={dto.PlaybackPositionTicks} pct={dto.PlayedPercentage?.ToString("0.##", CultureInfo.InvariantCulture) ?? "-"} unplayed={dto.UnplayedItemCount?.ToString(CultureInfo.InvariantCulture) ?? "-"}");

    private static string Show(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "-";

    private T Service<T>()
        where T : notnull
        => _factory.Services.GetRequiredService<T>();

    private async Task<Library> GetLibraryAsync()
    {
        await _setupLock.WaitAsync(TestContext.Current.CancellationToken);
        try
        {
            if (!_libraries.TryGetValue(_factory, out var library))
            {
                library = await CreateLibraryAsync();
                _libraries[_factory] = library;
            }

            return library;
        }
        finally
        {
            _setupLock.Release();
        }
    }

    private async Task<Library> CreateLibraryAsync()
    {
        await _factory.CreateAuthenticatedClientAsync();
        var manager = Service<ILibraryManager>();
        var root = Path.Combine(AcceptanceApplicationFactory.MediaFolder, "libraries-" + Path.GetFileName(_factory.Root));
        var existing = manager.GetVirtualFolders().Select(v => v.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var type in Enum.GetValues<CollectionTypeOptions>().Where(t => !existing.Contains(t.ToString())))
        {
            var folder = Path.Combine(root, type.ToString());
            Directory.CreateDirectory(folder);
            await manager.AddVirtualFolder(type.ToString(), type, new LibraryOptions { PathInfos = [new MediaPathInfo(folder)] }, false);
        }

        await manager.ValidateMediaLibrary(new Progress<double>(), CancellationToken.None);
        var folders = manager.GetUserRootFolder().Children.OfType<CollectionFolder>()
            .Where(f => f.CollectionType is not null)
            .OrderBy(f => f.Name, StringComparer.Ordinal)
            .ToDictionary(f => f.CollectionType!.Value.ToString(), f => f, StringComparer.Ordinal);

        T Add<T>(T item, Folder parent, string name, int order)
            where T : BaseItem
        {
            item.Id = new Guid(MD5.HashData(Encoding.UTF8.GetBytes("acceptance-library/" + name)));
            item.Name = name;
            item.SortName = name;
            item.Path = Path.Combine(parent.Path ?? root, name);
            item.ParentId = parent.Id;
            item.DateCreated = _epoch.AddDays(order);
            item.DateModified = _epoch.AddDays(order);
            item.PremiereDate = _epoch.AddDays(-order);
            item.RunTimeTicks = TimeSpan.FromMinutes(90).Ticks;
            manager.CreateItem(item, parent);
            return item;
        }

        var movies = Enumerable.Range(1, 5).Select(i => Add(new Movie(), folders["movies"], "Movie " + i, i)).ToArray();
        var series = Add(new Series(), folders["tvshows"], "Show S", 10);
        var season = Add(new Season { IndexNumber = 1, SeriesId = series.Id, SeriesName = series.Name }, series, "Season 1", 11);
        var episodes = Enumerable.Range(1, 3)
            .Select(i => Add(new Episode { IndexNumber = i, ParentIndexNumber = 1, SeriesId = series.Id, SeasonId = season.Id, SeriesName = series.Name }, season, "Episode " + i, 11 + i))
            .ToArray();
        var artist = Add(new MusicArtist(), folders["music"], "Artist A", 20);
        var album = Add(new MusicAlbum { AlbumArtists = ["Artist A"], Artists = ["Artist A"], Genres = ["Rock", "Pop"] }, folders["music"], "Album X", 21);
        var tracks = Enumerable.Range(1, 4)
            .Select(i => Add(
                new AudioItem { Album = "Album X", AlbumArtists = ["Artist A"], Artists = ["Artist A"], Genres = i % 2 == 0 ? ["Rock"] : ["Pop", "Jazz"], IndexNumber = i },
                album,
                "Track " + i,
                21 + i))
            .ToArray();

        return new Library(folders, movies, series, episodes, artist, album, tracks);
    }

    private sealed record Library(
        Dictionary<string, CollectionFolder> Folders,
        Movie[] Movies,
        Series Series,
        Episode[] Episodes,
        MusicArtist Artist,
        MusicAlbum Album,
        AudioItem[] Tracks);
}
