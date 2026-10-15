using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.MediaEncoding.Transcoding;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

using AudioItem = MediaBrowser.Controller.Entities.Audio.Audio;

namespace Jellyfin.Server.Integration.Tests.Acceptance;

/// <summary>
/// The in-memory test server for the acceptance tests: ffmpeg launches are replaced by a
/// <see cref="RecordingTranscodeManager"/>, and library items are created directly.
/// </summary>
public sealed class AcceptanceApplicationFactory : JellyfinApplicationFactory
{
    private readonly SemaphoreSlim _setupLock = new(1, 1);
    private string? _accessToken;

    public AcceptanceApplicationFactory()
        : this(Path.Combine(Path.GetTempPath(), "jellyfin-test-data", "acceptance-host-" + Path.GetFileNameWithoutExtension(Path.GetRandomFileName())))
    {
    }

    private AcceptanceApplicationFactory(string root)
        : base(root)
    {
        Root = root;
    }

    /// <summary>
    /// Gets the server's root folder, which appears in recorded paths and is masked there.
    /// </summary>
    public string Root { get; }

    /// <summary>
    /// Gets the folder holding the real media files of <see cref="TestItem.RealFile"/> items, which
    /// appears in recorded paths and is masked there.
    /// </summary>
    public static string MediaFolder { get; } = Path.Combine(Path.GetTempPath(), "jellyfin-acceptance-media");

    /// <summary>
    /// Gets the access token of the authenticated client, once one has been created.
    /// </summary>
    public string AccessToken => _accessToken ?? string.Empty;

    /// <summary>
    /// Gets the recording transcode manager the server uses.
    /// </summary>
    public RecordingTranscodeManager Transcodes => Services.GetRequiredService<RecordingTranscodeManager>();

    /// <summary>
    /// Creates a client authenticated as the server's administrator.
    /// </summary>
    /// <returns>The client.</returns>
    public async Task<HttpClient> CreateAuthenticatedClientAsync()
    {
        var client = CreateClient();
        _accessToken ??= await AuthHelper.CompleteStartupAsync(client);
        client.DefaultRequestHeaders.AddAuthHeader(_accessToken);
        return client;
    }

    /// <summary>
    /// Creates the library item for a test item, once, with its media streams.
    /// </summary>
    /// <param name="testItem">The test item.</param>
    /// <returns>The item id.</returns>
    public async Task<Guid> EnsureItemAsync(TestItem testItem)
    {
        var libraryManager = Services.GetRequiredService<ILibraryManager>();
        var extension = testItem.IsAudio ? ".flac" : ".mkv";
        string path;
        Guid id;
        if (testItem.RealFile)
        {
            // A real file, so direct streaming works; its id is fixed because the path is not.
            path = Path.Combine(MediaFolder, testItem.Name + extension);
            id = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes("acceptance/" + testItem.Name)).AsSpan(0, 16));
        }
        else
        {
            path = "/acceptance/media/" + testItem.Name + extension;
            id = libraryManager.GetNewItemId(path, testItem.IsAudio ? typeof(AudioItem) : typeof(Movie));
        }

        await _setupLock.WaitAsync(TestContext.Current.CancellationToken);
        try
        {
            if (libraryManager.GetItemById(id) is null)
            {
                if (testItem.RealFile)
                {
                    Directory.CreateDirectory(MediaFolder);
                    await File.WriteAllBytesAsync(path, Encoding.ASCII.GetBytes("fLaC acceptance test content " + testItem.Name), TestContext.Current.CancellationToken);
                }

                var root = libraryManager.RootFolder;
                BaseItem item = testItem.IsAudio ? new AudioItem() : new Movie();
                item.Id = id;
                item.Name = testItem.Name;
                item.Path = path;
                item.Container = testItem.IsAudio ? "flac" : "mkv";
                item.RunTimeTicks = TimeSpan.FromSeconds(20).Ticks;
                item.ParentId = root.Id;
                item.DateCreated = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                item.DateModified = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                libraryManager.CreateItem(item, root);
                Services.GetRequiredService<IMediaStreamRepository>().SaveMediaStreams(id, testItem.Streams(), CancellationToken.None);
            }
        }
        finally
        {
            _setupLock.Release();
        }

        return id;
    }

    /// <summary>
    /// Masks what differs between runs and machines: the access token, the server root and the
    /// media folder, each as is, with forward slashes and in ffmpeg's filter escaping (C\:/...).
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The masked text.</returns>
    public string Mask(string text)
    {
        if (_accessToken is not null)
        {
            text = text.Replace(_accessToken, "<token>", StringComparison.Ordinal);
        }

        foreach (var (folder, label) in new[] { (Root, "<root>"), (MediaFolder, "<media>") })
        {
            var forward = folder.Replace('\\', '/');
            text = text.Replace(folder, label, StringComparison.Ordinal)
                .Replace(folder.Replace("\\", "\\\\", StringComparison.Ordinal), label, StringComparison.Ordinal)
                .Replace(forward.Replace(":", "\\:", StringComparison.Ordinal), label, StringComparison.Ordinal)
                .Replace(forward, label, StringComparison.Ordinal);
        }

        return text;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _setupLock.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ITranscodeManager>();
            services.AddSingleton<TranscodeManager>();
            services.AddSingleton<RecordingTranscodeManager>();
            services.AddSingleton<ITranscodeManager>(sp => sp.GetRequiredService<RecordingTranscodeManager>());
        });
    }
}
