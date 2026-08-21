using System;
using System.Collections.Generic;
using System.Linq;
using TransitGuard.Core.Abstractions;

namespace TransitGuard.Core.Journeys;

/// <summary>
/// Umstiegs-Suche v2 (Empfehlung aus 20-build-log): 1 Umstieg, zeitfenster-geprüft.
/// Strategie: Abfahrtsfenster am Start (default 90 min) → Beine A gruppiert nach
/// Umstieghalt → Beine B ab Umstieghalt nach Ankunft+Puffer → beste Gesamtankunft.
/// Komplexitätsdeckel: max. Beine je Stufe, kürzeste Haltedistanzen zuerst.
/// </summary>
public sealed class TransferRouter(ITripScheduleStore schedules)
{
    public sealed record TransferLeg(string TripId, string StartDate, string RouteId, string? Headsign,
        string BoardStopId, string AlightStopId, DateTimeOffset BoardAt, DateTimeOffset AlightAt);
    public sealed record TransferConnection(TransferLeg LegA, TransferLeg LegB,
        string TransferStopId, int WaitSeconds, int TotalSeconds);

    private static DateTimeOffset DayStart(string d) =>
        DateTimeOffset.TryParseExact(d, "yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var x) ? x
            : throw new InvalidOperationException("start_date");

    public IReadOnlyList<TransferConnection> FindWithOneTransfer(
        string cityId, string startDate, string fromStopId, string toStopId,
        DateTimeOffset now, int windowMinutes = 90, int maxResults = 5, int transferBufferSeconds = 120)
    {
        var dayStart = DayStart(startDate);
        var deadline = now.AddMinutes(windowMinutes);

        // Bein-A: Fahrten ab Start im Fenster (bis 45 min Fahrt je Bein)
        var legsA = new List<(TripSchedule S, int IdxFrom, DateTimeOffset Dep)>();
        foreach (var (_, s) in schedules.All(cityId))
        {
            var i = s.IndexOf(fromStopId);
            if (i is null) continue;
            var dep = dayStart.AddSeconds(s.Stops[i.Value].DepartureS);
            if (dep < now.AddSeconds(-30) || dep > deadline) continue;
            legsA.Add((s, i.Value, dep));
        }

        var results = new List<TransferConnection>();
        foreach (var (sa, idxA, depA) in legsA.Take(400))   // Deckel: Top 400 Abfahrten
        {
            for (int j = idxA + 1; j < sa.Stops.Count; j++)
            {
                var transferStop = sa.Stops[j].StopId;
                if (transferStop == toStopId) continue;                       // Direktfahrt — nicht Sache des Routers
                var arriveA = dayStart.AddSeconds(sa.Stops[j].ArrivalS);
                if (arriveA - depA > TimeSpan.FromMinutes(45)) break;          // Bein-A max 45 min

                // Bein-B: ab Umstieghalt nach Ankunft+Puffer, muss Ziel bedienen
                foreach (var (_, sb) in schedules.All(cityId))
                {
                    var iB = sb.IndexOf(transferStop);
                    if (iB is null) continue;
                    var iTo = sb.IndexOf(toStopId);
                    if (iTo is null || iTo <= iB) continue;
                    var depB = dayStart.AddSeconds(sb.Stops[iB.Value].DepartureS);
                    if (depB < arriveA.AddSeconds(transferBufferSeconds) || depB > deadline.AddMinutes(45)) continue;
                    var arriveB = dayStart.AddSeconds(sb.Stops[iTo.Value].ArrivalS);
                    var total = (int)(arriveB - depA).TotalSeconds;
                    if (total <= 0 || total > 3 * 3600) continue;

                    results.Add(new TransferConnection(
                        new TransferLeg(sa.TripId, startDate, sa.RouteId, sa.Headsign, fromStopId, transferStop, depA, arriveA),
                        new TransferLeg(sb.TripId, startDate, sb.RouteId, sb.Headsign, transferStop, toStopId, depB, arriveB),
                        transferStop, (int)(depB - arriveA).TotalSeconds, total));
                }
                if (results.Count > 4000) break;
            }
        }
        return results.OrderBy(r => r.TotalSeconds)
            .GroupBy(r => (r.LegA.RouteId, r.TransferStopId, r.LegB.RouteId))
            .Select(g => g.First())
            .Take(maxResults)
            .ToList();
    }
}
