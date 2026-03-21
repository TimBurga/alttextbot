using AltTextBot.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace AltTextBot.Integration.Tests;

public sealed class DatabaseFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public AltTextBotDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AltTextBotDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;
        return new AltTextBotDbContext(options);
    }

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();
}
