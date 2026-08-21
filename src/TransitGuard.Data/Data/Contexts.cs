using Microsoft.EntityFrameworkCore;
using TransitGuard.Core.Models;

namespace TransitGuard.Data;

// ============================================================================
// EF-Core-Mappings auf das SQL-Schema (docs/sql/0001-0004). KEINE EF-Migrationen —
// Schema ist per migrate.sh kanonisch (ADR-0012). Nur die von der API berührten Tabellen.
// Prod-Repositories (tg_app) + Billing-Kontext (tg_billing) — Ticket M4 verdrahtet Program.
// ============================================================================

public sealed class ReportRow
{
    public Guid Id { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string CityId { get; set; } = default!;
    public short AnchorType { get; set; }
    public string? StationId { get; set; }
    public string? StationName { get; set; }
    public string? TripId { get; set; }
    public string? TripStartDate { get; set; }
    public string? RouteId { get; set; }
    public short? DirectionId { get; set; }
    public string? Headsign { get; set; }
    public short ReportType { get; set; }
    public short VehicleKind { get; set; }
    public short InspectorCount { get; set; }
    public Guid ReporterDeviceId { get; set; }
    public decimal ReporterTrust { get; set; }
    public string Status { get; set; } = "active";
    public DateTimeOffset ExpiresAt { get; set; }
    public int TtlBaseSeconds { get; set; }
    public int Confirmations { get; set; }
    public int Contradictions { get; set; }
    public Guid? ParentReportId { get; set; }
    public string Origin { get; set; } = "user";
    public bool Visible { get; set; } = true;
    public NetTopologySuite.Geometries.Point? Geo { get; set; }   // geography(point,4326) — Schema 0001
    public string Metadata { get; set; } = "{}";
    public string? LastKnownStationId { get; set; }
}

public sealed class DeviceRow
{
    public Guid DeviceId { get; set; }
    public byte[] TokenHash { get; set; } = default!;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastSeenAt { get; set; }
    public bool IsBlocked { get; set; }
}

public sealed class TrustRow
{
    public Guid DeviceId { get; set; }
    public decimal Score { get; set; } = 50m;
    public int TotalReports { get; set; }
    public int ConfirmedCount { get; set; }
    public int FalseCount { get; set; }
    public int ContradictedCount { get; set; }
    public short Rank { get; set; } = 1;
    public DateTimeOffset? LastStateChange { get; set; }
}

public sealed class ReportEventRow
{
    public long EventId { get; set; }
    public string CityId { get; set; } = default!;
    public Guid ReportId { get; set; }
    public string EventType { get; set; } = default!;
    public Guid? ActorDeviceId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string Payload { get; set; } = "{}";
}

public sealed class RtTripStateRow
{
    public string CityId { get; set; } = default!;
    public string TripId { get; set; } = default!;
    public string TripStartDate { get; set; } = default!;
    public string RouteId { get; set; } = default!;
    public string? LastStopId { get; set; }
    public int? LastDelayS { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset FeedSeenAt { get; set; }
}

public sealed class FeatureFlagRow { public string Key { get; set; } = default!; public bool Enabled { get; set; } public bool IsPublic { get; set; } = true; }

public sealed class EntitlementRow
{
    public Guid Id { get; set; }
    public string Tier { get; set; } = "free";
    public string Status { get; set; } = "active";
    public string Store { get; set; } = "none";
    public byte[]? StoreRefHash { get; set; }
    public byte[] AccessTokenHash { get; set; } = default!;
    public byte[] RestoreCodeHash { get; set; } = default!;
    public short ConnectionsCap { get; set; } = 3;
    public DateTimeOffset? ValidUntil { get; set; }
    public DateTimeOffset? PurchasedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}

/// <summary>App-Kontext (Rolle tg_app). Achtung: reports ist partitioniert — PK (id, created_at).</summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<ReportRow> Reports => Set<ReportRow>();
    public DbSet<DeviceRow> Devices => Set<DeviceRow>();
    public DbSet<TrustRow> TrustScores => Set<TrustRow>();
    public DbSet<ReportEventRow> ReportEvents => Set<ReportEventRow>();
    public DbSet<FeatureFlagRow> FeatureFlags => Set<FeatureFlagRow>();
    public DbSet<Data.RtTripStateRow> RtTripState => Set<RtTripStateRow>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        ApplySnakeCase(b);
        b.Entity<ReportRow>(e =>
        {
            e.ToTable("reports");
            e.HasKey(x => new { x.Id, x.CreatedAt });
            e.Property(x => x.Metadata).HasColumnType("jsonb");
            e.Property(x => x.Geo).HasColumnType("geography(point,4326)");
            e.Property(x => x.TtlBaseSeconds).HasColumnName("ttl_base_s");   // Kanon-Spaltenname 0001
            e.Property(x => x.ReporterTrust).HasColumnType("numeric(6,2)");
        });
        b.Entity<DeviceRow>(e => { e.ToTable("devices"); e.HasKey(x => x.DeviceId); e.Property(x => x.TokenHash).HasColumnType("bytea"); });
        b.Entity<TrustRow>(e => { e.ToTable("trust_scores"); e.HasKey(x => x.DeviceId); e.Property(x => x.Score).HasColumnType("numeric(6,2)"); });
        b.Entity<ReportEventRow>(e => { e.ToTable("report_events"); e.HasKey(x => x.EventId); e.Property(x => x.Payload).HasColumnType("jsonb"); });
        b.Entity<FeatureFlagRow>(e => { e.ToTable("feature_flags"); e.HasKey(x => x.Key); });
        b.Entity<RtTripStateRow>(e =>
        {
            e.ToTable("rt_trip_state");
            e.HasKey(x => new { x.CityId, x.TripId, x.TripStartDate });
        });
    }

    // Konvention: PascalCase-Properties -> snake_case-Spalten (Schema-Kanon, sql/0001-0004)
    private static void ApplySnakeCase(ModelBuilder b)
    {
        foreach (var entity in b.Model.GetEntityTypes())
            foreach (var prop in entity.GetProperties())
                prop.SetColumnName(string.Concat(prop.Name.Select((c, i) =>
                    i > 0 && char.IsUpper(c) ? "_" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString())));
    }

}



/// <summary>Billing-Kontext (Rolle tg_billing — Zweitverbindung; RLS-geschützt, 0003 §2).</summary>
public sealed class BillingDbContext(DbContextOptions<BillingDbContext> options) : DbContext(options)
{
    public DbSet<EntitlementRow> Entitlements => Set<EntitlementRow>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        ApplySnakeCase(b);
        b.Entity<EntitlementRow>(e =>
        {
            e.ToTable("entitlements");
            e.HasKey(x => x.Id);
            e.Property(x => x.StoreRefHash).HasColumnType("bytea");
            e.Property(x => x.AccessTokenHash).HasColumnType("bytea");
            e.Property(x => x.RestoreCodeHash).HasColumnType("bytea");
            e.HasIndex(x => x.AccessTokenHash).IsUnique();
            e.HasIndex(x => x.RestoreCodeHash).IsUnique();
        });
    }

    // Konvention: PascalCase-Properties -> snake_case-Spalten (Schema-Kanon, sql/0001-0004)
    private static void ApplySnakeCase(ModelBuilder b)
    {
        foreach (var entity in b.Model.GetEntityTypes())
            foreach (var prop in entity.GetProperties())
                prop.SetColumnName(string.Concat(prop.Name.Select((c, i) =>
                    i > 0 && char.IsUpper(c) ? "_" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString())));
    }

}
