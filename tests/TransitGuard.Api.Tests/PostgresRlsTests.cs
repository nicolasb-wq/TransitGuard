using Npgsql;
using Xunit;

namespace TransitGuard.Api.Tests;

/// <summary>
/// T-PG-RLS: Row Level Security gegen eine ECHTE Postgres-Instanz.
///
/// Bis 22.08.2026 stand RLS auf Belegen aus einer früheren Session, nicht auf
/// eigener Messung. Diese Tests prüfen die Isolation als Verhalten — und sie
/// werden rot, wenn die Policy abgeschaltet wird (nachgewiesen, 27-build-log §B).
/// </summary>
[Collection("postgres")]
public sealed class PostgresRlsTests
{
    private readonly PostgresFixture _pg;
    public PostgresRlsTests(PostgresFixture pg) => _pg = pg;

    private bool Ueberspringen(out string grund)
    {
        grund = _pg.Grund ?? "";
        return !_pg.Verfuegbar;
    }

    [SkippableFact]
    public void T_PG_RLS_1_tg_app_sieht_keine_Entitlements()
    {
        Skip.If(Ueberspringen(out var g), g);
        using var con = _pg.Oeffne("tg_app");
        // Doppelt abgeriegelt: kein GRANT und zusätzlich FORCE ROW LEVEL SECURITY.
        var ex = Record.Exception(() => PostgresFixture.Skalar(con, "SELECT count(*) FROM entitlements"));
        Assert.NotNull(ex);
        Assert.Contains("permission denied", ex!.Message, StringComparison.OrdinalIgnoreCase);
    }

    [SkippableFact]
    public void T_PG_RLS_2_tg_billing_sieht_Entitlements()
    {
        // Gegenprobe zu RLS_1: die Sperre liegt an der Rolle, nicht an einer
        // kaputten Verbindung oder einer fehlenden Tabelle.
        Skip.If(Ueberspringen(out var g), g);
        using var con = _pg.Oeffne("tg_billing");
        var n = PostgresFixture.Skalar(con, "SELECT count(*) FROM entitlements");
        Assert.NotNull(n);
    }

    [SkippableFact]
    public void T_PG_RLS_3_feature_flags_zeilenweise_getrennt()
    {
        Skip.If(Ueberspringen(out var g), g);
        using var admin = _pg.Oeffne();
        PostgresFixture.Ausfuehren(admin,
            "INSERT INTO feature_flags(key, enabled, is_public) VALUES "
            + "('t_pg_rls_oeffentlich', true, true), ('t_pg_rls_intern', true, false) "
            + "ON CONFLICT (key) DO UPDATE SET is_public = EXCLUDED.is_public");
        try
        {
            using var app = _pg.Oeffne("tg_app");
            var sichtbar = (long)PostgresFixture.Skalar(app,
                "SELECT count(*) FROM feature_flags WHERE key LIKE 't_pg_rls_%'")!;
            // Das ist echte ZEILEN-Trennung: dieselbe Tabelle, dieselbe Abfrage,
            // unterschiedliche Sicht je Rolle.
            Assert.Equal(1, sichtbar);

            var intern = (long)PostgresFixture.Skalar(app,
                "SELECT count(*) FROM feature_flags WHERE key = 't_pg_rls_intern'")!;
            Assert.Equal(0, intern);

            using var ops = _pg.Oeffne("tg_ops");
            var alle = (long)PostgresFixture.Skalar(ops,
                "SELECT count(*) FROM feature_flags WHERE key LIKE 't_pg_rls_%'")!;
            Assert.Equal(2, alle);
        }
        finally
        {
            PostgresFixture.Ausfuehren(admin, "DELETE FROM feature_flags WHERE key LIKE 't_pg_rls_%'");
        }
    }

    [SkippableFact]
    public void T_PG_RLS_4_Kill_Switch_Stufe_3_versteckt_Meldungen_inaktiver_Staedte()
    {
        // Die Policy ist zugleich die dritte Kill-Switch-Stufe (docs/09, ADR-0009):
        // Stadt deaktiviert ⇒ Meldungen unsichtbar, selbst wenn ein Endpunkt den
        // Feature-Flag-Check vergisst. Genau das wird hier gemessen.
        Skip.If(Ueberspringen(out var g), g);
        using var admin = _pg.Oeffne();
        var stadt = "t_pg_rls_stadt";
        // bbox ist echte PostGIS-Geometrie (geometry(Polygon,4326)) — hier wird
        // sie mit erzeugt, das ist zugleich ein Beleg, dass PostGIS scharf ist.
        PostgresFixture.Ausfuehren(admin,
            $"INSERT INTO cities(city_id, slug, display_name, is_active, gtfs_static_source, bbox) "
            + $"VALUES ('{stadt}', '{stadt}', 'Testallee', true, 'test', "
            + "ST_MakeEnvelope(9.0, 53.0, 10.0, 54.0, 4326)) "
            + "ON CONFLICT (city_id) DO UPDATE SET is_active = true");
        var id = Guid.NewGuid();
        PostgresFixture.Ausfuehren(admin,
            $"INSERT INTO reports(id, created_at, city_id, anchor_type, report_type, vehicle_kind, "
            + $"station_id, inspector_count, reporter_device_id, reporter_trust, status, expires_at, ttl_base_s) "
            + $"VALUES ('{id}', now(), '{stadt}', 1, 2, 1, 'X1', 1, gen_random_uuid(), 50, 'active', now() + interval '30 min', 1800)");
        try
        {
            using var app = _pg.Oeffne("tg_app");
            var sichtbarAktiv = (long)PostgresFixture.Skalar(app,
                $"SELECT count(*) FROM reports WHERE id = '{id}'")!;
            Assert.Equal(1, sichtbarAktiv);

            PostgresFixture.Ausfuehren(admin, $"UPDATE cities SET is_active = false WHERE city_id = '{stadt}'");
            using var app2 = _pg.Oeffne("tg_app");
            var sichtbarInaktiv = (long)PostgresFixture.Skalar(app2,
                $"SELECT count(*) FROM reports WHERE id = '{id}'")!;
            Assert.Equal(0, sichtbarInaktiv);

            // Der Datensatz ist NICHT gelöscht — nur unsichtbar für die App-Rolle.
            var nochDa = (long)PostgresFixture.Skalar(admin,
                $"SELECT count(*) FROM reports WHERE id = '{id}'")!;
            Assert.Equal(1, nochDa);
        }
        finally
        {
            PostgresFixture.Ausfuehren(admin, $"DELETE FROM reports WHERE id = '{id}'");
            PostgresFixture.Ausfuehren(admin, $"DELETE FROM cities WHERE city_id = '{stadt}'");
        }
    }

    [SkippableFact]
    public void T_PG_RLS_5_gesperrte_Geraete_sind_fuer_die_App_unsichtbar()
    {
        Skip.If(Ueberspringen(out var g), g);
        using var admin = _pg.Oeffne();
        var offen = Guid.NewGuid();
        var gesperrt = Guid.NewGuid();
        PostgresFixture.Ausfuehren(admin,
            $"INSERT INTO devices(device_id, token_hash, is_blocked) VALUES "
            + $"('{offen}', decode(md5('t1{offen:N}'), 'hex'), false), "
            + $"('{gesperrt}', decode(md5('t2{gesperrt:N}'), 'hex'), true)");
        try
        {
            using var app = _pg.Oeffne("tg_app");
            Assert.Equal(1L, PostgresFixture.Skalar(app, $"SELECT count(*) FROM devices WHERE device_id = '{offen}'"));
            Assert.Equal(0L, PostgresFixture.Skalar(app, $"SELECT count(*) FROM devices WHERE device_id = '{gesperrt}'"));
        }
        finally
        {
            PostgresFixture.Ausfuehren(admin, $"DELETE FROM devices WHERE device_id IN ('{offen}','{gesperrt}')");
        }
    }
}
