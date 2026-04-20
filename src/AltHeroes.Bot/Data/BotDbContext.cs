using Microsoft.EntityFrameworkCore;

namespace AltHeroes.Bot.Data;

public sealed class BotDbContext(DbContextOptions<BotDbContext> options) : DbContext(options)
{
    public DbSet<SubscriberEntity> Subscribers => Set<SubscriberEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SubscriberEntity>(e =>
        {
            e.ToTable("subscribers");
            e.HasKey(s => s.Did);
            e.Property(s => s.Did).HasColumnName("did").HasMaxLength(256);
            e.Property(s => s.RKey).HasColumnName("r_key").HasMaxLength(128).IsRequired();
            e.Property(s => s.Active).HasColumnName("active");
            e.Property(s => s.CreatedAt).HasColumnName("created_at");
            e.Property(s => s.UpdatedAt).HasColumnName("updated_at");
        });
    }
}
