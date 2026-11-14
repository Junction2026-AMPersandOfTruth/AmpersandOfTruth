using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AmpersandOfTruth.Web.Features.WebSpread.Store;

/// <summary>
/// Response cache row (table <c>WebSpreadCache</c>): one Oxylabs search or fetch, keyed by the request hash. Only
/// <see cref="WebCallOutcome.Ok"/> responses are stored, so a demo replays with zero live calls and failures are retried.
/// </summary>
public sealed class WebCacheEntry
{
    /// <summary>PK. 64 lowercase hex chars: SHA-256 of the provider mode + kind + normalised request.</summary>
    public required string Key { get; init; }
    /// <summary><c>search</c> or <c>fetch</c>.</summary>
    public required string Kind { get; init; }
    /// <summary>Human-readable request (query + language + window, or the URL), for debugging the cache by hand.</summary>
    public required string Request { get; init; }
    /// <summary>The serialised <see cref="WebSearchResponse"/> or <see cref="FetchedPage"/>.</summary>
    public required string ResponseJson { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
}

/// <summary>Ledger row (table <c>WebSpreadCalls</c>): one per search/fetch, cached or live. The source of the request caps
/// after a restart, and of the request counter in the dev footer.</summary>
public sealed class WebCallRecord
{
    public long Id { get; set; }
    public required DateTimeOffset At { get; init; }
    /// <summary>The trace id (one incident); null = unscoped.</summary>
    public string? Scope { get; init; }
    public required string Kind { get; init; }
    /// <summary>Stored as string.</summary>
    public required WebCallOutcome Outcome { get; init; }
    public bool FromCache { get; init; }
    /// <summary>True when the request reached Oxylabs (counts towards the caps).</summary>
    public bool Live { get; init; }
    public required string Request { get; init; }
    public long LatencyMs { get; init; }
    public string? Error { get; init; }
}

public sealed class WebCacheEntryConfiguration : IEntityTypeConfiguration<WebCacheEntry>
{
    public void Configure(EntityTypeBuilder<WebCacheEntry> b)
    {
        b.ToTable("WebSpreadCache");
        b.HasKey(e => e.Key);
        b.Property(e => e.Key).HasMaxLength(64);
        b.Property(e => e.Kind).HasMaxLength(10).IsRequired();
        b.Property(e => e.Request).HasMaxLength(2000).IsRequired();
        b.HasIndex(e => e.Kind);
    }
}

public sealed class WebCallRecordConfiguration : IEntityTypeConfiguration<WebCallRecord>
{
    public void Configure(EntityTypeBuilder<WebCallRecord> b)
    {
        b.ToTable("WebSpreadCalls");
        b.HasKey(e => e.Id);
        b.Property(e => e.Kind).HasMaxLength(10).IsRequired();
        b.Property(e => e.Outcome).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.Scope).HasMaxLength(200);
        b.Property(e => e.Request).HasMaxLength(2000).IsRequired();
        b.Property(e => e.Error).HasMaxLength(1000);
        b.HasIndex(e => e.At);
        b.HasIndex(e => e.Scope);
    }
}
