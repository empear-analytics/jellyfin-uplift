using System.Collections.Concurrent;

namespace Jellyfin.Server.Integration.Tests.Acceptance;

/// <summary>
/// The requests the stub upstream server has received, one line each.
/// </summary>
public sealed class UpstreamLog
{
    public ConcurrentQueue<string> Requests { get; } = new();
}
