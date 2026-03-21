using AltTextBot.Integration.Tests;
using Microsoft.EntityFrameworkCore;

namespace AltTextBot.Integration.Tests;

public class MigrationTests(DatabaseFixture db) : IClassFixture<DatabaseFixture>
{
    [Fact]
    public async Task Migrations_ApplyCleanly()
    {
        await using var context = db.CreateDbContext();
        var pending = await context.Database.GetPendingMigrationsAsync();
        pending.Should().BeEmpty("all migrations should have been applied in fixture setup");
    }

    [Fact]
    public async Task Schema_CanReadAndWriteSubscribers()
    {
        await using var context = db.CreateDbContext();
        var subscriber = AltTextBot.Domain.Entities.Subscriber.Create("did:plc:migration-test", "migration.test");
        context.Subscribers.Add(subscriber);
        await context.SaveChangesAsync();

        var retrieved = await context.Subscribers.FindAsync("did:plc:migration-test");
        retrieved.Should().NotBeNull();
        retrieved!.Handle.Should().Be("migration.test");
    }
}
