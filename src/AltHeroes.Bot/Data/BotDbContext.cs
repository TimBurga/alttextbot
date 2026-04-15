using Microsoft.EntityFrameworkCore;

namespace AltHeroes.Bot.Data;

public sealed class BotDbContext(DbContextOptions<BotDbContext> options) : DbContext(options)
{
    public DbSet<SubscriberEntity> Subscribers => Set<SubscriberEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SubscriberEntity>(e =>
        {
            e.HasKey(s => s.Did);
            e.Property(s => s.Did).HasMaxLength(256);
            e.Property(s => s.RKey).HasMaxLength(128).IsRequired();
        });
    }
}
