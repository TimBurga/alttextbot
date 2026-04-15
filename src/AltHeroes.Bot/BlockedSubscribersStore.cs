using AltHeroes.Bot.Data;
using Microsoft.EntityFrameworkCore;

namespace AltHeroes.Bot;

/// <summary>
/// Persists a set of blocked DIDs to a SQLite database so blocks survive restarts.
/// Blocked subscribers stay enrolled (so unenroll-via-unlike still works)
/// but are skipped for scoring and labeling.
/// An in-memory cache is kept for fast hot-path lookups via <see cref="IsBlocked"/>.
/// </summary>
public sealed class BlockedSubscribersStore
{
    private readonly IDbContextFactory<BotDbContext> _dbFactory;
    private readonly ILogger<BlockedSubscribersStore> _logger;
    private readonly SemaphoreSlim _cacheLock = new(1, 1);
    private HashSet<string> _blocked = [];

    public BlockedSubscribersStore(IDbContextFactory<BotDbContext> dbFactory, ILogger<BlockedSubscribersStore> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    /// <summary>Loads all currently blocked DIDs from the database into the in-memory cache.</summary>
    public async Task LoadAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var blocked = await db.Subscribers
            .Where(s => s.Blocked)
            .Select(s => s.Did)
            .ToListAsync(ct);
        _blocked = [.. blocked];
        _logger.LogInformation("BlockedSubscribersStore: Loaded {Count} blocked DIDs.", _blocked.Count);
    }

    /// <summary>Returns true if the given DID is currently blocked (checked via in-memory cache).</summary>
    public bool IsBlocked(string did) => _blocked.Contains(did);

    public async Task BlockAsync(string did, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var now = DateTimeOffset.UtcNow;

        var existing = await db.Subscribers.FindAsync([did], ct);
        if (existing is null)
            db.Subscribers.Add(new SubscriberEntity { Did = did, Blocked = true, CreatedAt = now, UpdatedAt = now });
        else
        {
            existing.Blocked = true;
            existing.UpdatedAt = now;
        }
        await db.SaveChangesAsync(ct);

        await _cacheLock.WaitAsync(ct);
        try { _blocked.Add(did); }
        finally { _cacheLock.Release(); }

        _logger.LogInformation("BlockedSubscribersStore: Blocked {Did}.", did);
    }

    public async Task UnblockAsync(string did, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var existing = await db.Subscribers.FindAsync([did], ct);
        if (existing is not null)
        {
            existing.Blocked = false;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
        }

        await _cacheLock.WaitAsync(ct);
        try { _blocked.Remove(did); }
        finally { _cacheLock.Release(); }

        _logger.LogInformation("BlockedSubscribersStore: Unblocked {Did}.", did);
    }

    public IReadOnlySet<string> All() => _blocked;
}
