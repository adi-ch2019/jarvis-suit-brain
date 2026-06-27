using Microsoft.EntityFrameworkCore;
using Jarvis.Shared;

namespace Jarvis.SuitTelemetryApi.Data;

public class SuitDbContext : DbContext
{
    public SuitDbContext(DbContextOptions<SuitDbContext> options) : base(options) { }
    
    public DbSet<SuitStatusEvent> SuitTelemetry { get; set; }
    
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SuitStatusEvent>(entity =>
        {
            entity.HasKey(e => e.EventId);
            entity.HasIndex(e => e.SuitId).HasDatabaseName("IX_SuitId");
            entity.HasIndex(e => e.Timestamp).HasDatabaseName("IX_Timestamp");
            entity.Property(e => e.SuitId).HasMaxLength(50);
            entity.Property(e => e.Status).HasMaxLength(50);
        });
    }
}