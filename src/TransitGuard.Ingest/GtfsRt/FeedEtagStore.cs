using System.Collections.Concurrent;

namespace TransitGuard.Ingest.GtfsRt;

/// <summary>
/// Haelt den zuletzt gesehenen ETag je Feed-URL — prozessweit, nicht je Auftragsausfuehrung.
///
/// Warum es diese Klasse gibt: <see cref="PollRealtimeJob"/> ist in der API als <c>Scoped</c>
/// registriert. Hangfire oeffnet fuer JEDE Ausfuehrung einen eigenen Scope, also entstand bei
/// jedem Lauf eine frische Job-Instanz — und ein ETag in einem Instanzfeld war beim naechsten
/// Lauf immer wieder <c>null</c>. Der If-None-Match-Zweig existierte im Code, wurde in der
/// Produktion aber nie ausgefuehrt (gemessen 24.08.2026, T-ETAG-1).
///
/// Der Speicher muss deshalb ein Singleton sein. Bewusst ohne TTL: eine Handvoll Feed-URLs,
/// je ein kurzer String.
/// </summary>
public sealed class FeedEtagStore
{
    private readonly ConcurrentDictionary<string, string> _etags = new(StringComparer.Ordinal);

    public string? Get(string feedUrl) => _etags.TryGetValue(feedUrl, out var e) ? e : null;

    public void Set(string feedUrl, string? etag)
    {
        if (string.IsNullOrEmpty(etag)) _etags.TryRemove(feedUrl, out _);
        else _etags[feedUrl] = etag;
    }
}
