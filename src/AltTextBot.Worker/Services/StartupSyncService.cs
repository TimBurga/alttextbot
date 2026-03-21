using AltTextBot.Application.Configuration;
using AltTextBot.Application.Interfaces;
using AltTextBot.Domain.Enums;
using AltTextBot.Infrastructure.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AltTextBot.Worker.Services;

public class StartupSyncService(
    IServiceProvider serviceProvider,
    ISubscriberSet subscriberSet,
    IOptions<BotOptions> botOptions,
    ILogger<StartupSyncService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("StartupSyncService: Beginning startup sync...");

        using var scope = serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AltTextBotDbContext>();
        var subscriberService = scope.ServiceProvider.GetRequiredService<ISubscriberService>();
        var auditLogger = scope.ServiceProvider.GetRequiredService<IAuditLogger>();
        var blueskyApi = scope.ServiceProvider.GetRequiredService<IBlueskyApiClient>();
        var tapApiClient = scope.ServiceProvider.GetRequiredService<ITapApiClient>();

        try
        {
            // Fetch current likes from ATProto API (5-minute timeout guards against hung connections)
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            var currentLikers = await FetchCurrentLikersAsync(botOptions.Value, blueskyApi, linkedCts.Token);
            logger.LogInformation("StartupSyncService: Found {Count} current likers from ATProto API", currentLikers.Count);

            // Load current DB state into in-memory set before syncing so SubscribeAsync/UnsubscribeAsync
            // can maintain it incrementally throughout the sync.
            var dbActiveDids = await db.Subscribers
                .Where(s => s.Status == SubscriberStatus.Active)
                .Select(s => s.Did)
                .ToHashSetAsync(cancellationToken);

            subscriberSet.Initialize(dbActiveDids.ToList());

            var currentLikerDids = currentLikers.Keys.ToHashSet();

            // Users in API but not in DB → add as new subscribers
            var toAdd = currentLikerDids.Except(dbActiveDids).ToList();
            foreach (var did in toAdd)
            {
                var handle = currentLikers[did];
                await subscriberService.SubscribeAsync(did, handle, cancellationToken);
                logger.LogDebug("StartupSync: Added subscriber {Did} ({Handle})", did, handle);
            }

            // Users in DB as Active but not in API → deactivate
            var toDeactivate = dbActiveDids.Except(currentLikerDids).ToList();
            foreach (var did in toDeactivate)
            {
                await subscriberService.UnsubscribeAsync(did, cancellationToken);
                logger.LogDebug("StartupSync: Deactivated subscriber {Did}", did);
            }

            if (toAdd.Count > 0 || toDeactivate.Count > 0)
            {
                await auditLogger.LogAsync(
                    AuditEventType.StartupSync,
                    null,
                    $"Startup sync complete. Added: {toAdd.Count}, Deactivated: {toDeactivate.Count}",
                    cancellationToken);
            }

            // Bulk-register pre-existing subscribers with Tap. Newly added/reactivated subscribers
            // were already registered individually inside SubscribeAsync — exclude them to avoid
            // double-registration.
            var toAddSet = toAdd.ToHashSet();
            var preExistingDids = await db.Subscribers
                .Where(s => s.Status == SubscriberStatus.Active && !toAddSet.Contains(s.Did))
                .Select(s => s.Did)
                .ToListAsync(cancellationToken);

            if (preExistingDids.Count > 0)
            {
                try
                {
                    await tapApiClient.AddReposAsync(preExistingDids, cancellationToken);
                    logger.LogInformation("StartupSyncService: Registered {Count} pre-existing DIDs with Tap", preExistingDids.Count);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "StartupSyncService: Failed to register DIDs with Tap. TapWorker will not receive events until Tap is available.");
                }
            }

            var totalActive = preExistingDids.Count + toAdd.Count;
            subscriberSet.MarkInitialized();
            logger.LogInformation("StartupSyncService: Initialized subscriber set with {Count} active DIDs", totalActive);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "StartupSyncService: Error during startup sync. Marking set as initialized anyway to unblock JetstreamWorker.");
            // Still mark initialized so JetstreamWorker doesn't hang
            subscriberSet.MarkInitialized();
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Fetches all users who have liked the bot's own profile post.
    /// Returns a dictionary of DID -> Handle.
    /// </summary>
    private async Task<Dictionary<string, string>> FetchCurrentLikersAsync(
        BotOptions bot,
        IBlueskyApiClient blueskyApi,
        CancellationToken ct)
    {
        var result = new Dictionary<string, string>();

        if (string.IsNullOrWhiteSpace(bot.Did))
        {
            logger.LogWarning("StartupSync: Bot DID not configured. Skipping API fetch.");
            return result;
        }

        var profileUri = $"at://{bot.Did}/app.bsky.actor.profile/self";
        string? cursor = null;

        do
        {
            try
            {
                var page = await blueskyApi.GetLikesPageAsync(profileUri, cursor, ct);
                foreach (var like in page.Likes)
                    result[like.Did] = like.Handle;
                cursor = page.Cursor;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "StartupSync: Error fetching likes page. Stopping pagination.");
                break;
            }
        }
        while (cursor is not null);

        return result;
    }
}
