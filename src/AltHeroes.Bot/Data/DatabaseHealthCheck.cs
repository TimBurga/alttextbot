using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AltHeroes.Bot.Data;

public sealed class DatabaseHealthCheck<TDbContext> : IHealthCheck where TDbContext : DbContext
{
    private readonly IDbContextFactory<TDbContext> _factory;

    public DatabaseHealthCheck(IDbContextFactory<TDbContext> factory)
    {
        _factory = factory;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var db = _factory.CreateDbContext();
            var canConnect = await db.Database.CanConnectAsync(cancellationToken);
            return canConnect
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("Cannot connect to database");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Database health check failed", ex);
        }
    }
}

public static class DatabaseHealthCheckExtensions
{
    public static IHealthChecksBuilder AddPostgreSqlHealthCheck<TDbContext>(
        this IHealthChecksBuilder builder) where TDbContext : DbContext
    {
        return builder.AddCheck<DatabaseHealthCheck<TDbContext>>(
            "PostgreSQL",
            tags: new[] { "db" });
    }
}
