using AltTextBot.Domain.Entities;
using AltTextBot.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace AltTextBot.Infrastructure.Data;

public class AltTextBotDbContext(DbContextOptions<AltTextBotDbContext> options) : DbContext(options)
{
    public DbSet<Subscriber> Subscribers => Set<Subscriber>();
    public DbSet<TrackedPost> TrackedPosts => Set<TrackedPost>();
    public DbSet<TrackedImage> TrackedImages => Set<TrackedImage>();
    public DbSet<FirehoseState> FirehoseStates => Set<FirehoseState>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Subscriber
        modelBuilder.Entity<Subscriber>(e =>
        {
            e.HasKey(s => s.Did);
            e.Property(s => s.Did).HasMaxLength(100);
            e.Property(s => s.Handle).HasMaxLength(300);
            e.Property(s => s.Status).HasConversion<string>();
            e.HasMany(s => s.TrackedPosts)
                .WithOne(p => p.Subscriber)
                .HasForeignKey(p => p.SubscriberDid);
        });

        // TrackedPost
        modelBuilder.Entity<TrackedPost>(e =>
        {
            e.HasKey(p => p.AtUri);
            e.Property(p => p.AtUri).HasMaxLength(300);
            e.Property(p => p.SubscriberDid).HasMaxLength(100);
            e.HasIndex(p => new { p.SubscriberDid, p.CreatedAt });
            e.HasIndex(p => p.CreatedAt);
            e.HasMany(p => p.Images)
                .WithOne(i => i.Post)
                .HasForeignKey(i => i.PostAtUri)
                .HasPrincipalKey(p => p.AtUri)
                .OnDelete(DeleteBehavior.Cascade);
            e.Navigation(p => p.Images).HasField("_images");
        });

        // TrackedImage
        modelBuilder.Entity<TrackedImage>(e =>
        {
            e.HasKey(i => i.Id);
            e.Property(i => i.PostAtUri).HasMaxLength(300);
            e.Property(i => i.BlobCid).HasMaxLength(200);
            e.HasIndex(i => i.PostAtUri);
        });

        // FirehoseState (single-row)
        modelBuilder.Entity<FirehoseState>(e =>
        {
            e.HasKey(f => f.Id);
        });

        // AuditLog
        modelBuilder.Entity<AuditLog>(e =>
        {
            e.HasKey(a => a.Id);
            e.Property(a => a.SubscriberDid).HasMaxLength(100);
            e.Property(a => a.EventType).HasConversion<string>();
            e.HasIndex(a => a.SubscriberDid);
            e.HasIndex(a => a.Timestamp);
        });
    }
}
