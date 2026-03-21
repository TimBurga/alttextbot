using AltTextBot.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace AltTextBot.Integration.Tests;

public sealed class LabelerDatabaseFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public LabelerDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<LabelerDbContext>()
            .UseNpgsql(ConnectionString)
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .Options;
        return new LabelerDbContext(options);
    }

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await using var db = CreateDbContext();
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();
}
