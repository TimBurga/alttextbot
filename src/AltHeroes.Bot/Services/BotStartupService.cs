using AltHeroes.Bot;
using AltHeroes.Bot.Configuration;
using AltHeroes.Bot.Data;
using AltHeroes.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AltHeroes.Bot.Services;

/// <summary>
/// Runs the startup sequence before JetstreamWorker begins consuming events:
///   1. Load active subscribers from database → populate BotState.
///   2. Query current Ozone labels for all subscribers.
///   3. Parallel backfill (10 concurrent): score each subscriber → diff → apply.
///   4. Signal JetstreamWorker via StartupGate.
/// </summary>
/// <remarks>
/// Profile likes cannot be backfilled from any Bluesky API (app.bsky.feed.getLikes
/// does not support profile URIs). The database is the only source of truth for
/// previously-discovered subscribers; new subscribers arrive via Jetstream.
/// </remarks>
public sealed class BotStartupService(
    BotState state,
    IDbContextFactory<BotDbContext> dbContextFactory,
    ListRecordsClient listRecords,
    OzoneClient ozone,
    LabelDiffService diff,
    StartupGate startupGate,
    IOptions<ScoringOptions> scoringOptions,
    ILogger<BotStartupService> logger) : BackgroundService
{
    private readonly ScoringConfig _scoringConfig = scoringOptions.Value.ToConfig();

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        logger.LogInformation("BotStartupService: Starting...");

        // 1. Load active subscribers from database
        logger.LogInformation("BotStartupService: Loading active subscribers from database...");
        await using (var db = dbContextFactory.CreateDbContext())
        {
            var activeSubscribers = await db.Subscribers
                .Where(s => s.Active)
                .ToListAsync(ct);

            foreach (var subscriber in activeSubscribers)
                state.Enroll(subscriber.Did, subscriber.RKey);
        }

        logger.LogInformation("BotStartupService: {Count} subscribers loaded from database.", state.SubscriberCount);

        // 2 + 3. Backfill all subscribers concurrently (10 at a time)
        var allDids = state.AllSubscriberDids();
        logger.LogInformation("BotStartupService: Starting backfill for {Count} subscribers...", allDids.Count);

        using var sem = new SemaphoreSlim(10);
        var tasks = allDids.Select(did => BackfillOneAsync(did, sem, ct));
        await Task.WhenAll(tasks);

        logger.LogInformation("BotStartupService: Backfill complete.");

        // 4. Signal Jetstream to start
        startupGate.MarkComplete();
    }

    private async Task BackfillOneAsync(string did, SemaphoreSlim sem, CancellationToken ct)
    {
        await sem.WaitAsync(ct);
        try
        {
            // Query current Ozone label
            var currentTier = await ozone.QueryCurrentTierAsync(did, ct);
            state.SetCurrentTier(did, currentTier);

            // Fetch posts and score
            var posts = await listRecords.GetPostsAsync(did, _scoringConfig.WindowDays, ct);
            var result = ScoringService.ComputeTier(posts, _scoringConfig, DateTimeOffset.UtcNow);

            // Apply if changed (handle = did as fallback; handle resolution not needed at startup)
            await diff.ApplyIfChangedAsync(did, did, result.Tier, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "BotStartupService: Backfill failed for {Did}.", did);
        }
        finally { sem.Release(); }
    }
}
