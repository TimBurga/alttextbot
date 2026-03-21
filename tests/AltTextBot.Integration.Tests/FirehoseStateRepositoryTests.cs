using AltTextBot.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace AltTextBot.Integration.Tests;

public class FirehoseStateRepositoryTests(DatabaseFixture fixture)
    : IClassFixture<DatabaseFixture>, IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await using var db = fixture.CreateDbContext();
        var existing = await db.FirehoseStates.FirstOrDefaultAsync();
        if (existing is not null)
        {
            db.FirehoseStates.Remove(existing);
            await db.SaveChangesAsync();
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GetCursorAsync_WhenNoState_ReturnsZero()
    {
        await using var db = fixture.CreateDbContext();
        var repo = new FirehoseStateRepository(db);

        var cursor = await repo.GetCursorAsync();

        cursor.Should().Be(0);
    }

    [Fact]
    public async Task SaveCursorAsync_WhenNoStateExists_CreatesRow()
    {
        await using var db = fixture.CreateDbContext();
        var repo = new FirehoseStateRepository(db);

        await repo.SaveCursorAsync(123456789L);

        await using var verify = fixture.CreateDbContext();
        var state = await verify.FirehoseStates.FirstOrDefaultAsync();
        state.Should().NotBeNull();
        state!.LastTimeUs.Should().Be(123456789L);
    }

    [Fact]
    public async Task SaveCursorAsync_WhenStateExists_UpdatesRow()
    {
        await using var db = fixture.CreateDbContext();
        var repo = new FirehoseStateRepository(db);
        await repo.SaveCursorAsync(100L);

        await using var db2 = fixture.CreateDbContext();
        var repo2 = new FirehoseStateRepository(db2);
        await repo2.SaveCursorAsync(200L);

        await using var verify = fixture.CreateDbContext();
        var states = await verify.FirehoseStates.ToListAsync();
        states.Should().HaveCount(1);
        states[0].LastTimeUs.Should().Be(200L);
    }
}
