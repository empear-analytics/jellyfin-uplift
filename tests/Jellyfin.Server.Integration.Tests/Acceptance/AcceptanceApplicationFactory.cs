using System.IO;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.MediaEncoding.Transcoding;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Jellyfin.Server.Integration.Tests.Acceptance;

/// <summary>
/// The in-memory test server, with ffmpeg launches replaced by a <see cref="RecordingTranscodeManager"/>.
/// </summary>
public sealed class AcceptanceApplicationFactory : JellyfinApplicationFactory
{
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
    /// Gets the recording transcode manager the server uses.
    /// </summary>
    public RecordingTranscodeManager Transcodes => Services.GetRequiredService<RecordingTranscodeManager>();

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
