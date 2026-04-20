using AltHeroes.Bot.Configuration;
using AltHeroes.Bot.Data;
using AltHeroes.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AltHeroes.Bot.Services;

/// <summary>
/// Runs the startup sequence before JetstreamWorker begins consuming events:
///   1. Load active subscribers from database → populate BotState.
///   2. Parallel backfill (10 concurrent): score each subscriber → diff → apply.
///   3. Signal JetstreamWorker via StartupGate.
///   4. Resolve missing rkeys for legacy subscriber rows in the background.
/// </summary>
public sealed class BotStartupService(
    BotState state,
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

        // 1. Load active subscribers from database
        logger.LogInformation("BotStartupService: Loading active subscribers from database...");
        List<SubscriberEntity> activeSubscribers;
        await using (var db = dbContextFactory.CreateDbContext())
        {
            activeSubscribers = await db.Subscribers
                .Where(s => s.Active)
                .ToListAsync(ct);
        }

        // 2. Enroll all active subscribers (empty rkeys are safe — BotState skips indexing them)
        foreach (var subscriber in activeSubscribers)
            state.Enroll(subscriber.Did, subscriber.RKey);

        logger.LogInformation("BotStartupService: {Count} subscribers loaded from database.", state.SubscriberCount);

        // 3. Backfill scores concurrently (10 at a time)
        var allDids = state.AllSubscriberDids();
        logger.LogInformation("BotStartupService: Starting backfill for {Count} subscribers...", allDids.Count);

        using var sem = new SemaphoreSlim(10);
        var tasks = allDids.Select(did => BackfillOneAsync(did, sem, ct));
        await Task.WhenAll(tasks);

        logger.LogInformation("BotStartupService: Backfill complete.");

        // 4. Signal Jetstream to start
        startupGate.MarkComplete();

        // 5. Resolve rkeys for legacy subscribers in the background (non-blocking)
        var legacy = activeSubscribers.Where(s => string.IsNullOrEmpty(s.RKey)).ToList();
        if (legacy.Count > 0)
            _ = BackfillRkeysAsync(legacy, ct);
    }

    private async Task BackfillRkeysAsync(List<SubscriberEntity> legacy, CancellationToken ct)
    {
        logger.LogInformation("BotStartupService: Resolving rkeys for {Count} legacy subscribers in background...", legacy.Count);
        var resolved = 0;
        try
        {
            await using var db = dbContextFactory.CreateDbContext();
            foreach (var subscriber in legacy)
            {
                if (ct.IsCancellationRequested) break;
                var rkey = await listRecords.GetLikeRkeyAsync(subscriber.Did, _labelerDid, ct);
                if (rkey is null)
                {
                    logger.LogWarning("BotStartupService: Could not resolve rkey for legacy subscriber {Did}.", subscriber.Did);
                    continue;
                }

                subscriber.RKey = rkey;
                subscriber.UpdatedAt = DateTimeOffset.UtcNow;
                state.Enroll(subscriber.Did, rkey); // adds to rkey index now that we have it
                var entry = db.Attach(subscriber);
                entry.Property(s => s.RKey).IsModified = true;
                entry.Property(s => s.UpdatedAt).IsModified = true;
                resolved++;
            }

            if (resolved > 0)
                await db.SaveChangesAsync(ct);
        }
        catch (OperationCanceledException) { /* shutdown during backfill — acceptable */ }
        catch (Exception ex)
        {
            logger.LogError(ex, "BotStartupService: Background rkey backfill failed.");
        }

        logger.LogInformation("BotStartupService: Background rkey backfill complete — {Resolved}/{Total} resolved.", resolved, legacy.Count);
    }

    private async Task BackfillOneAsync(string did, SemaphoreSlim sem, CancellationToken ct)
    {
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
