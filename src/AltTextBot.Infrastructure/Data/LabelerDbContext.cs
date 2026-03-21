using Microsoft.EntityFrameworkCore;

namespace AltTextBot.Infrastructure.Data;

/// <summary>
/// Read-only access to the external labeler's PostgreSQL database.
/// No migrations — maps to existing labeler schema.
/// </summary>
public class LabelerDbContext(DbContextOptions<LabelerDbContext> options) : DbContext(options)
{
    public DbSet<LabelerLabel> Labels => Set<LabelerLabel>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LabelerLabel>(e =>
        {
            e.ToTable("labels");
            e.HasKey(l => l.Id);
            e.Property(l => l.Id).HasColumnName("id");
            e.Property(l => l.Did).HasColumnName("did").HasMaxLength(100);
            e.Property(l => l.Val).HasColumnName("val").HasMaxLength(100);
            e.Property(l => l.Neg).HasColumnName("neg");
        });
    }
}

public class LabelerLabel
{
    public int Id { get; set; }
    public string Did { get; set; } = default!;
    public string Val { get; set; } = default!;
    public bool Neg { get; set; }
}
