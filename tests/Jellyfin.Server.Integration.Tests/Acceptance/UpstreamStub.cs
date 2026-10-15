using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Server.Integration.Tests.Acceptance;

/// <summary>
/// A stand-in for a remote media server, so remote streams can be tested without the network.
/// Each file name is a scenario that answers range requests differently; other hosts are passed on.
/// </summary>
public sealed class UpstreamStub : DelegatingHandler
{
    public const string BaseUrl = "http://upstream.acceptance.test/media/";

    private readonly UpstreamLog _log;

    public UpstreamStub(UpstreamLog log)
    {
        _log = log;
    }

    /// <summary>
    /// Gets the scenarios: "plain" ignores ranges, "ranges" honours them and says so, "partial-only"
    /// honours them without saying so, "no-ranges" says it does not, "untyped" sends no content type
    /// or length, and "missing" is not found.
    /// </summary>
    public static IReadOnlyList<string> Scenarios { get; } = ["plain", "ranges", "partial-only", "no-ranges", "untyped", "missing"];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri!.ToString();
        if (!url.StartsWith(BaseUrl, StringComparison.Ordinal))
        {
            return base.SendAsync(request, cancellationToken);
        }

        var scenario = url[BaseUrl.Length..].Replace(".flac", string.Empty, StringComparison.Ordinal);
        var range = request.Headers.Range;
        _log.Requests.Enqueue($"GET {url[BaseUrl.Length..]} range={range?.ToString() ?? "-"}");

        var body = Encoding.ASCII.GetBytes("fLaC remote content of " + scenario);
        var honoursRange = scenario is "ranges" or "partial-only";
        var response = new HttpResponseMessage(scenario == "missing" ? HttpStatusCode.NotFound : HttpStatusCode.OK) { RequestMessage = request };
        if (honoursRange && range?.Ranges.FirstOrDefault() is { } wanted)
        {
            var from = (int)(wanted.From ?? 0);
            var to = (int)Math.Min(wanted.To ?? (body.Length - 1), body.Length - 1);
            response.StatusCode = HttpStatusCode.PartialContent;
            response.Content = new ByteArrayContent(body[from..(to + 1)]);
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(from, to, body.Length);
        }
        else
        {
            response.Content = new ByteArrayContent(scenario == "missing" ? Encoding.ASCII.GetBytes("not found") : body);
        }

        switch (scenario)
        {
            case "ranges":
                response.Headers.AcceptRanges.Add("bytes");
                break;
            case "no-ranges":
                response.Headers.AcceptRanges.Add("none");
                break;
            case "untyped":
                response.Content.Headers.ContentLength = null;
                break;
        }

        if (scenario != "untyped")
        {
            response.Content.Headers.ContentType = new MediaTypeHeaderValue(scenario == "missing" ? "text/plain" : "audio/flac");
        }

        return Task.FromResult(response);
    }
}
