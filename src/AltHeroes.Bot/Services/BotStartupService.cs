using AltHeroes.Bot;
using AltHeroes.Bot.Configuration;
using AltHeroes.Core;
using Microsoft.Extensions.Options;

namespace AltHeroes.Bot.Services;

/// <summary>
/// Runs the startup sequence before JetstreamWorker begins consuming events:
///   1. Load blocked subscribers from disk.
///   2. Enumerate all likes on the labeler profile → populate BotState.
///   3. Query current Ozone labels for all subscribers.
///   4. Parallel backfill (10 concurrent): score each subscriber → diff → apply.
///   5. Signal JetstreamWorker via StartupComplete.
/// </summary>
/// <summary>
/// Runs as a BackgroundService so it does not block host startup.
/// JetstreamWorker waits on StartupGate before processing events.
/// </summary>
public sealed class BotStartupService(
    BotState state,
    BlockedSubscribersStore blocked,
    ListRecordsClient listRecords,
    OzoneClient ozone,
    LabelDiffService diff,
    StartupGate startupGate,
    IOptions<BotOptions> botOptions,
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

        // 2. Enumerate all likes on labeler profile
        logger.LogInformation("BotStartupService: Fetching all subscribers from labeler profile likes...");
        var likerEntries = await listRecords.GetProfileLikesAsync(_labelerDid, ct);
        foreach (var (did, rkey) in likerEntries)
            state.Enroll(did, rkey);

        logger.LogInformation("BotStartupService: {Count} subscribers enrolled.", state.SubscriberCount);

        // 3 + 4. Backfill all subscribers concurrently (10 at a time)
        var allDids = state.AllSubscriberDids();
        logger.LogInformation("BotStartupService: Starting backfill for {Count} subscribers...", allDids.Count);

        using var sem = new SemaphoreSlim(10);
        var tasks = allDids.Select(did => BackfillOneAsync(did, sem, ct));
        await Task.WhenAll(tasks);

        logger.LogInformation("BotStartupService: Backfill complete.");

        // 5. Signal Jetstream to start
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
