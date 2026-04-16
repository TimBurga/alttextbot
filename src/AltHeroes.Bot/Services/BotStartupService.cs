using AltHeroes.Bot;
using AltHeroes.Bot.Configuration;
using AltHeroes.Bot.Data;
using AltHeroes.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AltHeroes.Bot.Services;

/// <summary>
/// Runs the startup sequence before JetstreamWorker begins consuming events:
///   1. Load blocked subscribers from disk.
///   2. Load active subscribers from database → populate BotState.
///   3. Backfill missing rkeys for legacy subscriber rows that lack one.
///   4. Query current Ozone labels for all subscribers.
///   5. Parallel backfill (10 concurrent): score each subscriber → diff → apply.
///   6. Signal JetstreamWorker via StartupGate.
/// </summary>
public sealed class BotStartupService(
    BotState state,
    BlockedSubscribersStore blocked,
    IDbContextFactory<BotDbContext> dbContextFactory,
    ListRecordsClient listRecords,
    OzoneClient ozone,
    LabelDiffService diff,
    StartupGate startupGate,
    IOptions<LabelerOptions> labelerOptions,
    IOptions<ScoringOptions> scoringOptions,
    ILogger<BotStartupService> logger) : BackgroundService
{
    private readonly string _labelerDid = labelerOptions.Value.Did;
    private readonly ScoringConfig _scoringConfig = scoringOptions.Value.ToConfig();

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        logger.LogInformation("BotStartupService: Starting...");

        // 1. Load blocked list
        await blocked.LoadAsync(ct);

        // 2. Load active subscribers from database
        logger.LogInformation("BotStartupService: Loading active subscribers from database...");
        List<SubscriberEntity> activeSubscribers;
        await using (var db = dbContextFactory.CreateDbContext())
        {
            activeSubscribers = await db.Subscribers
                .Where(s => s.Active)
                .ToListAsync(ct);
        }

        // 3. Backfill rkeys for any legacy rows that don't have one
        var needsRkey = activeSubscribers.Where(s => string.IsNullOrEmpty(s.RKey)).ToList();
        if (needsRkey.Count > 0)
        {
            logger.LogInformation("BotStartupService: Backfilling rkeys for {Count} legacy subscribers...", needsRkey.Count);
            await using var db = dbContextFactory.CreateDbContext();
            var resolved = 0;
            foreach (var subscriber in needsRkey)
            {
                var rkey = await listRecords.GetLikeRkeyAsync(subscriber.Did, _labelerDid, ct);
                if (rkey is not null)
                {
                    subscriber.RKey = rkey;
                    subscriber.UpdatedAt = DateTimeOffset.UtcNow;
                    var entry = db.Attach(subscriber);
                    entry.Property(s => s.RKey).IsModified = true;
                    entry.Property(s => s.UpdatedAt).IsModified = true;
                    resolved++;
                }
                else
                {
                    logger.LogWarning("BotStartupService: Could not resolve rkey for legacy subscriber {Did}.", subscriber.Did);
                }
            }
            if (resolved > 0)
                await db.SaveChangesAsync(ct);
            logger.LogInformation("BotStartupService: Backfilled rkeys for {Resolved}/{Total} legacy subscribers.", resolved, needsRkey.Count);
        }

        // 4. Enroll all active subscribers in memory
        foreach (var subscriber in activeSubscribers)
            state.Enroll(subscriber.Did, subscriber.RKey);

        logger.LogInformation("BotStartupService: {Count} subscribers loaded from database.", state.SubscriberCount);

        // 5. Backfill all subscribers concurrently (10 at a time)
        var allDids = state.AllSubscriberDids();
        logger.LogInformation("BotStartupService: Starting backfill for {Count} subscribers...", allDids.Count);

        using var sem = new SemaphoreSlim(10);
        var tasks = allDids.Select(did => BackfillOneAsync(did, sem, ct));
        await Task.WhenAll(tasks);

        logger.LogInformation("BotStartupService: Backfill complete.");

        // 6. Signal Jetstream to start
        startupGate.MarkComplete();
    }

    private async Task BackfillOneAsync(string did, SemaphoreSlim sem, CancellationToken ct)
    {
        if (blocked.IsBlocked(did))
        {
            logger.LogDebug("BotStartupService: Skipping blocked subscriber {Did}.", did);
            return;
        }

        await sem.WaitAsync(ct);
        try
        {
            var currentTier = await ozone.QueryCurrentTierAsync(did, ct);
            state.SetCurrentTier(did, currentTier);

            var posts = await listRecords.GetPostsAsync(did, _scoringConfig.WindowDays, ct);
            var result = ScoringService.ComputeTier(posts, _scoringConfig, DateTimeOffset.UtcNow);

            await diff.ApplyIfChangedAsync(did, did, result.Tier, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "BotStartupService: Backfill failed for {Did}.", did);
        }
        finally { sem.Release(); }
    }
}
