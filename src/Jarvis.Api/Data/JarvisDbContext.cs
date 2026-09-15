using Jarvis.Shared;
using Microsoft.EntityFrameworkCore;

namespace Jarvis.Api.Data;

public class JarvisDbContext(DbContextOptions<JarvisDbContext> options)
    : DbContext(options)
{
    public DbSet<TelemetryEvent> Telemetry => Set<TelemetryEvent>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<TelemetryEvent>(e =>
        {
            e.HasKey(x => new { x.SuitId, x.Timestamp });
            e.Property(x => x.SuitId).HasMaxLength(64);
            e.Property(x => x.Threat).HasMaxLength(256);
            e.HasIndex(x => x.Timestamp);
        });
    }
}