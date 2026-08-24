using System.Globalization;

namespace TransitGuard.Verifikation.IngestProbe;

/// <summary>
/// Speichermessung über /proc/self/status. VmHWM ist der Spitzenwert seit Prozessstart bzw.
/// seit dem letzten Reset über /proc/self/clear_refs (Wert 5 setzt den Peak zurück, Linux ≥ 4.0).
/// Ohne diesen Reset wäre jede Phasenmessung nur der Peak ALLER vorherigen Phasen — genau der
/// Fehler, der eine Messung wertlos macht.
/// </summary>
public static class Mess
{
    public static long RssKb() => ProcStatus("VmRSS");
    public static long PeakRssKb() => ProcStatus("VmHWM");

    /// <summary>Setzt VmHWM auf den aktuellen VmRSS. Gibt false zurück, wenn der Kernel das nicht zulässt.</summary>
    public static bool PeakZuruecksetzen()
    {
        try
        {
            var vorher = PeakRssKb();
            File.WriteAllText("/proc/self/clear_refs", "5\n");
            var nachher = PeakRssKb();
            // Gegenprobe der Messmethode selbst: der Reset muss den Peak wirklich gesenkt haben,
            // sonst melden wir das statt still falsch zu messen.
            return nachher <= vorher;
        }
        catch { return false; }
    }

    private static long ProcStatus(string feld)
    {
        foreach (var z in File.ReadLines("/proc/self/status"))
            if (z.StartsWith(feld + ":", StringComparison.Ordinal))
                return long.Parse(z.Split(':')[1].Replace("kB", "").Trim(), CultureInfo.InvariantCulture);
        return -1;
    }

    public static string Mb(long kb) => (kb / 1024.0).ToString("F1", CultureInfo.InvariantCulture) + " MB";
    public static string MbB(long bytes) => (bytes / 1048576.0).ToString("F1", CultureInfo.InvariantCulture) + " MB";

    public static long GcAlloziertBytes() => GC.GetTotalAllocatedBytes(precise: false);
    public static long GcHeapBytes() => GC.GetTotalMemory(forceFullCollection: false);

    /// <summary>CPU-Zeit des Prozesses (user+sys) in ms. (net8: Environment.CpuUsage gibt es erst ab net9.)</summary>
    public static double CpuMs()
    {
        using var p = System.Diagnostics.Process.GetCurrentProcess();
        return p.TotalProcessorTime.TotalMilliseconds;
    }

    public sealed record Phase(string Name, long PeakRssKb, long RssKb, long AllocBytes, double CpuMs, long WallMs);

    public static Phase Messen(string name, Action arbeit)
    {
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        PeakZuruecksetzen();
        var a0 = GcAlloziertBytes();
        var c0 = CpuMs();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        arbeit();
        sw.Stop();
        return new Phase(name, PeakRssKb(), RssKb(), GcAlloziertBytes() - a0, CpuMs() - c0, sw.ElapsedMilliseconds);
    }

    public static void Zeile(Phase p) => Console.WriteLine(
        $"  {p.Name,-38} Peak-RSS {Mb(p.PeakRssKb),10}  RSS danach {Mb(p.RssKb),10}  " +
        $"alloziert {MbB(p.AllocBytes),10}  CPU {p.CpuMs,8:F0} ms  Wand {p.WallMs,7} ms");
}
