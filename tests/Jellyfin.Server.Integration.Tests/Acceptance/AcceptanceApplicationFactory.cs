using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Emby.Server.Implementations.ScheduledTasks.Tasks;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.MediaEncoding.Transcoding;
using MediaBrowser.Model.Tasks;
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
    /// Gets the requests the stub upstream server has received.
    /// </summary>
    public UpstreamLog Upstream => Services.GetRequiredService<UpstreamLog>();

    /// <summary>
    /// Creates a client authenticated as the server's administrator.
    /// </summary>
    /// <returns>The client.</returns>
    public async Task<HttpClient> CreateAuthenticatedClientAsync()
    {
        var client = CreateClient();
        if (_accessToken is null)
        {
            await _setupLock.WaitAsync(TestContext.Current.CancellationToken);
            try
            {
                _accessToken ??= await CompleteStartupAsync(client);
            }
            finally
            {
                _setupLock.Release();
            }
        }

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
        if (testItem.RealFile || testItem.RemoteUrl is not null)
        {
            // A real file, so direct streaming works, or a remote stream; the id is fixed because
            // a file's path is not.
            path = testItem.RemoteUrl ?? Path.Combine(MediaFolder, testItem.Name + extension);
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
    /// Completes the startup wizard and waits for the library scan that completing it queues.
    /// </summary>
    /// <remarks>
    /// The server queues that scan in the background (LibraryManager.ConfigurationUpdated). The
    /// scan deletes every item whose file it does not find on disk, which is every item
    /// <see cref="EnsureItemAsync"/> creates, so an item created while it runs could disappear and
    /// later requests for it return 404. That happened when the machine was loaded and the scan
    /// was slow; waiting for it to finish before any item is created makes the order fixed.
    /// </remarks>
    /// <param name="client">An unauthenticated client.</param>
    /// <returns>The administrator's access token.</returns>
    private async Task<string> CompleteStartupAsync(HttpClient client)
    {
        var taskManager = Services.GetRequiredService<ITaskManager>();
        var scanned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnTaskCompleted(object? sender, TaskCompletionEventArgs e)
        {
            if (e.Task.ScheduledTask is RefreshMediaLibraryTask)
            {
                scanned.TrySetResult();
            }
        }

        taskManager.TaskCompleted += OnTaskCompleted;
        try
        {
            var accessToken = await AuthHelper.CompleteStartupAsync(client);
            await scanned.Task.WaitAsync(TimeSpan.FromMinutes(5), TestContext.Current.CancellationToken);
            return accessToken;
        }
        finally
        {
            taskManager.TaskCompleted -= OnTaskCompleted;
        }
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

            // Remote media is fetched from the stub upstream server; other hosts are reached as usual.
            services.AddSingleton<UpstreamLog>();
            services.AddHttpClient(NamedClient.Default)
                .ConfigurePrimaryHttpMessageHandler(sp => new UpstreamStub(sp.GetRequiredService<UpstreamLog>())
                {
                    InnerHandler = new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.All }
                });
        });
    }
}
