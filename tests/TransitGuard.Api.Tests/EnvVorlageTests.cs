using System.Text.RegularExpressions;
using Xunit;

namespace TransitGuard.Api.Tests;

/// <summary>
/// T-ENV-1 — Jeder Konfigurationsschluessel, den der Code LIEST, muss in
/// <c>deploy/env.example</c> stehen.
///
/// Warum das ein Test ist und keine Doku-Regel: am 24.08.2026 fehlten in der Vorlage
/// <c>Data__Provider</c>, <c>Jobs__Storage</c> und <c>Ingest__Enabled</c> vollstaendig.
/// Ein Erstdeploy nach dieser Vorlage waere im In-Memory-Modus hochgekommen — mit
/// eingerichteter, aber ungenutzter Datenbank und ohne Ingest. Nichts haette gemeldet,
/// dass etwas fehlt; die API waere „gruen" gewesen und haette nur keine Daten gehabt.
/// Ausserdem hiess der Feed-Schluessel <c>INGEST__FEED_URL</c> und traf damit
/// <c>Ingest:FEED_URL</c> statt <c>Ingest:FeedUrl</c> — still wirkungslos.
/// </summary>
public sealed class EnvVorlageTests
{
    /// <summary>Env-Name → Config-Schluessel: "__" wird ":", der Rest bleibt.</summary>
    private static string AlsConfigSchluessel(string envName) => envName.Replace("__", ":");

    private static HashSet<string> GeleseneSchluessel()
    {
        var wurzel = ApiFixtureHelfer.RepoWurzel();
        var treffer = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Genau die Formen, in denen der Code heute Konfiguration liest.
        var muster = new Regex(
            """(?:Configuration|cfg)\s*\[\s*"([^"]+)"\s*\]|(?:Configuration|cfg)\.GetValue<[^>]+>\(\s*"([^"]+)"\s*\)""",
            RegexOptions.Compiled);
        foreach (var datei in Directory.EnumerateFiles(Path.Combine(wurzel, "src"), "*.cs", SearchOption.AllDirectories))
        {
            if (datei.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
             || datei.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;
            foreach (Match m in muster.Matches(File.ReadAllText(datei)))
            {
                var k = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
                if (k.Contains(':')) treffer.Add(k);   // nur echte Abschnittsschluessel, keine Einzelwoerter
            }
        }
        return treffer;
    }

    private static HashSet<string> VorlagenSchluessel()
    {
        var pfad = Path.Combine(ApiFixtureHelfer.RepoWurzel(), "deploy", "env.example");
        var s = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var z in File.ReadAllLines(pfad))
        {
            var t = z.Trim();
            if (t.Length == 0 || t.StartsWith('#')) continue;
            var i = t.IndexOf('=');
            if (i > 0) s.Add(AlsConfigSchluessel(t[..i].Trim()));
        }
        return s;
    }

    [Fact]
    public void T_ENV_1_Alle_gelesenen_Schluessel_stehen_in_der_Vorlage()
    {
        var gelesen = GeleseneSchluessel();
        Assert.NotEmpty(gelesen);   // sonst prueft der Test nichts (Regex kaputt)

        var vorlage = VorlagenSchluessel();
        var fehlend = gelesen.Where(k => !vorlage.Contains(k)).OrderBy(k => k).ToList();

        Assert.True(fehlend.Count == 0,
            "Diese Schluessel liest der Code, aber deploy/env.example kennt sie nicht — ein Deploy nach " +
            "dieser Vorlage laeuft still mit den Vorgabewerten: " + string.Join(", ", fehlend));
    }

    /// <summary>
    /// T-ENV-2 — Die drei Betriebsschalter muessen in der Vorlage auf Produktionswerte
    /// stehen. Ein auskommentiertes oder leeres Data__Provider ist die gefaehrlichste
    /// Variante: die API startet, nur eben ohne Datenbank und ohne Ingest.
    /// </summary>
    [Fact]
    public void T_ENV_2_Betriebsschalter_stehen_auf_Produktionswerten()
    {
        var pfad = Path.Combine(ApiFixtureHelfer.RepoWurzel(), "deploy", "env.example");
        var zeilen = File.ReadAllLines(pfad)
            .Select(z => z.Trim())
            .Where(z => z.Length > 0 && !z.StartsWith('#'))
            .ToDictionary(z => z[..z.IndexOf('=')].Trim(), z => z[(z.IndexOf('=') + 1)..].Trim(),
                          StringComparer.OrdinalIgnoreCase);

        Assert.Equal("postgres", zeilen.GetValueOrDefault("Data__Provider"));
        Assert.Equal("postgres", zeilen.GetValueOrDefault("Jobs__Storage"));
        Assert.Equal("true", zeilen.GetValueOrDefault("Ingest__Enabled"));
    }

    /// <summary>
    /// T-ENV-3 — Kein Schluessel in der Vorlage darf einen echten Wert enthalten.
    /// Die Datei ist eingecheckt; ein versehentlich gefuelltes Passwort waere ein Leck.
    /// </summary>
    [Fact]
    public void T_ENV_3_Vorlage_enthaelt_keine_echten_Geheimnisse()
    {
        var pfad = Path.Combine(ApiFixtureHelfer.RepoWurzel(), "deploy", "env.example");
        foreach (var z in File.ReadAllLines(pfad))
        {
            var t = z.Trim();
            if (t.Length == 0 || t.StartsWith('#')) continue;
            var i = t.IndexOf('=');
            var name = t[..i].Trim();
            var wert = t[(i + 1)..].Trim();
            if (!name.Contains("PASS", StringComparison.OrdinalIgnoreCase)
             && !name.Contains("SECRET", StringComparison.OrdinalIgnoreCase)
             && !name.Contains("KEY", StringComparison.OrdinalIgnoreCase)
             && !name.Contains("DATABASE", StringComparison.OrdinalIgnoreCase)) continue;
            Assert.True(wert.Length == 0 || wert.Contains("CHANGE", StringComparison.Ordinal),
                $"{name} in deploy/env.example sieht nach einem echten Wert aus: „{wert}“");
        }
    }
}
