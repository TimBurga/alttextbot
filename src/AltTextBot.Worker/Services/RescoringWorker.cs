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

public class RescoringWorker(
    IServiceProvider serviceProvider,
    ISubscriberSet subscriberSet,
    IOptions<ScoringOptions> scoringOptions,
    ILogger<RescoringWorker> logger) : BackgroundService
{
    private readonly ScoringOptions _options = scoringOptions.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await subscriberSet.WaitForInitializationAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunRescoringCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "RescoringWorker: Unhandled error during rescore cycle.");
            }

            await Task.Delay(TimeSpan.FromMinutes(_options.RescoringIntervalMinutes), stoppingToken);
        }
    }

    private async Task RunRescoringCycleAsync(CancellationToken ct)
    {
        logger.LogInformation("RescoringWorker: Starting rescore cycle.");
        var started = DateTimeOffset.UtcNow;

        using var scope = serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AltTextBotDbContext>();
        var scoringService = scope.ServiceProvider.GetRequiredService<IScoringService>();
        var labelerClient = scope.ServiceProvider.GetRequiredService<ILabelerClient>();
        var labelStateReader = scope.ServiceProvider.GetRequiredService<ILabelStateReader>();
        var postTracking = scope.ServiceProvider.GetRequiredService<IPostTrackingService>();
        var auditLogger = scope.ServiceProvider.GetRequiredService<IAuditLogger>();
        var postClient = scope.ServiceProvider.GetRequiredService<IBlueskyPostClient>();

        // 1. Clean up old posts (outside the rolling window)
        var cutoff = DateTimeOffset.UtcNow.AddDays(-_options.RollingWindowDays);
        await postTracking.DeleteOldPostsAsync(cutoff, ct);
        logger.LogInformation("RescoringWorker: Cleaned posts older than {Cutoff}", cutoff);

        // 2. Get all active subscribers
        var subscribers = await db.Subscribers
            .Where(s => s.Status == Domain.Enums.SubscriberStatus.Active)
            .ToListAsync(ct);

        logger.LogInformation("RescoringWorker: Rescoring {Count} active subscribers.", subscribers.Count);

        var changed = 0;

        foreach (var subscriber in subscribers)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                // Compute current score
                var score = await scoringService.ComputeScoreAsync(subscriber.Did, ct);

                // Read current label from labeler DB
                var currentLabel = await labelStateReader.GetCurrentLabelAsync(subscriber.Did, ct);
                var effectiveCurrent = currentLabel ?? LabelTier.None;

                // Apply label change if needed
                if (score.Tier != effectiveCurrent)
                {
                    logger.LogInformation(
                        "RescoringWorker: {Did} tier change {Old} → {New} (score: {Score:F1}%)",
                        subscriber.Did, effectiveCurrent, score.Tier, score.ScorePercent);

                    // Apply new label first — if this fails, old label remains (no regression)
                    if (score.Tier != LabelTier.None)
                    {
                        await labelerClient.ApplyLabelAsync(subscriber.Did, score.Tier, ct);
                        await auditLogger.LogAsync(AuditEventType.LabelApplied, subscriber.Did,
                            $"Applied label: {score.Tier}. Score: {score.ScorePercent:F1}%", ct);
                    }

                    // Negate old label after new one is confirmed applied
                    if (effectiveCurrent != LabelTier.None)
                    {
                        try
                        {
                            await labelerClient.NegateLabelAsync(subscriber.Did, effectiveCurrent, ct);
                            await auditLogger.LogAsync(AuditEventType.LabelNegated, subscriber.Did,
                                $"Negated label: {effectiveCurrent}", ct);
                        }
                        catch (Exception ex)
                        {
                            logger.LogWarning(ex, "RescoringWorker: Failed to negate old label {Label} for {Did}. Will retry next cycle.",
                                effectiveCurrent, subscriber.Did);
                        }
                    }

                    await auditLogger.LogAsync(AuditEventType.LabelChanged, subscriber.Did,
                        $"Tier changed: {effectiveCurrent} → {score.Tier}. Score: {score.ScorePercent:F1}% ({score.PostsWithAllAlt}/{score.TotalImagePosts} posts)", ct);

                    // Post congrats on Bluesky for upgrades only
                    if (score.Tier > effectiveCurrent && score.Tier != LabelTier.None)
                        await postClient.PostCongratsAsync(subscriber.Did, subscriber.Handle, score.Tier, ct);

                    changed++;
                }

                // Update LastScoredAt
                subscriber.RecordScored();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "RescoringWorker: Error rescoring subscriber {Did}.", subscriber.Did);
            }
        }

        await db.SaveChangesAsync(ct);

        var elapsed = DateTimeOffset.UtcNow - started;
        logger.LogInformation(
            "RescoringWorker: Cycle complete. {Total} subscribers, {Changed} label changes, elapsed: {Elapsed:g}",
            subscribers.Count, changed, elapsed);

        await auditLogger.LogAsync(AuditEventType.RescoringRun, null,
            $"Rescore cycle: {subscribers.Count} subscribers, {changed} changes, elapsed: {elapsed:g}", ct);
    }
}
