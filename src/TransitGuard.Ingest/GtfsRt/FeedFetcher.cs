namespace TransitGuard.Ingest.GtfsRt;

public sealed record FeedFetchResult(
    int StatusCode,
    byte[]? Body,
    string? ETag,
    bool NotModified,
    long ElapsedMs,
    string? Error);

/// <summary>
/// GTFS-RT-Abruf mit If-None-Match (ADR-0010: Header senden, aber keine Ersparnis einplanen —
/// gtfs.de regeneriert alle 10 s, 304 nur bei langsamen Feeds wie VBB, Messung N4).
/// </summary>
public sealed class FeedFetcher(HttpClient http)
{
    public async Task<FeedFetchResult> FetchAsync(string url, string? etag, CancellationToken ct = default)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrEmpty(etag)) req.Headers.IfNoneMatch.Add(System.Net.Http.Headers.EntityTagHeaderValue.Parse(etag));
            var sw = System.Diagnostics.Stopwatch.StartNew();
            using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (resp.StatusCode == System.Net.HttpStatusCode.NotModified)
                return new FeedFetchResult(304, null, resp.Headers.ETag?.ToString(), NotModified: true, sw.ElapsedMilliseconds, null);
            var bytes = await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            return new FeedFetchResult((int)resp.StatusCode, bytes, resp.Headers.ETag?.ToString(),
                NotModified: false, sw.ElapsedMilliseconds, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return new FeedFetchResult(0, null, etag, NotModified: false, 0, ex.Message);
        }
    }
}
