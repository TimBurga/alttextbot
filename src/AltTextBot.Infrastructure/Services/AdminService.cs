using AltTextBot.Application.Interfaces;
using AltTextBot.Domain.Entities;
using AltTextBot.Domain.Enums;
using AltTextBot.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AltTextBot.Infrastructure.Services;

public class AdminService(
    AltTextBotDbContext db,
    IScoringService scoringService,
    ILabelerClient labelerClient,
    ILabelStateReader labelStateReader,
    IAuditLogger auditLogger,
    IBlueskyPostClient postClient,
    ILogger<AdminService> logger) : IAdminService
{
    public async Task<PagedResult<Subscriber>> GetSubscribersAsync(int page, int pageSize, CancellationToken ct = default)
    {
        var total = await db.Subscribers.CountAsync(ct);
        var items = await db.Subscribers
            .OrderByDescending(s => s.SubscribedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new PagedResult<Subscriber>(items, total, page, pageSize);
    }

    public async Task<Subscriber?> GetSubscriberAsync(string did, CancellationToken ct = default)
        => await db.Subscribers.FindAsync([did], ct);

    public async Task ManualRescoreAsync(string did, CancellationToken ct = default)
    {
        var subscriber = await db.Subscribers.FindAsync([did], ct);
        if (subscriber is null) return;

        var score = await scoringService.ComputeScoreAsync(did, ct);
        var currentLabel = await labelStateReader.GetCurrentLabelAsync(did, ct);

        var effectiveCurrent = currentLabel ?? LabelTier.None;
        if (score.Tier != effectiveCurrent)
        {
            // Apply new label first — if this fails, old label remains (no regression)
            if (score.Tier != LabelTier.None)
                await labelerClient.ApplyLabelAsync(did, score.Tier, ct);

            // Negate old label after new one is confirmed applied
            if (currentLabel.HasValue && currentLabel.Value != LabelTier.None)
            {
                try
                {
                    await labelerClient.NegateLabelAsync(did, currentLabel.Value, ct);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "AdminService: Failed to negate old label {Label} for {Did}. Will be cleaned up on next rescore.", currentLabel.Value, did);
                }
            }

            await auditLogger.LogAsync(AuditEventType.LabelChanged, did,
                $"Manual rescore: {currentLabel} → {score.Tier}. Score: {score.ScorePercent:F1}%", ct);

            // Post congrats on Bluesky for upgrades only
            if (score.Tier > effectiveCurrent && score.Tier != LabelTier.None)
                await postClient.PostCongratsAsync(did, subscriber.Handle, score.Tier, ct);
        }

        subscriber.RecordScored();
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync(AuditEventType.ManualRescore, did,
            $"Score: {score.ScorePercent:F1}% ({score.PostsWithAllAlt}/{score.TotalImagePosts}). Tier: {score.Tier}", ct);
    }

    public async Task<IReadOnlyList<AuditLog>> GetRecentAuditLogsAsync(string did, int count, CancellationToken ct = default)
        => await db.AuditLogs
            .Where(a => a.SubscriberDid == did)
            .OrderByDescending(a => a.Timestamp)
            .Take(count)
            .ToListAsync(ct);

    public async Task<(IReadOnlyList<AuditLog> Logs, bool HasMore)> GetAuditLogsPageAsync(int page, int pageSize, CancellationToken ct = default)
    {
        var batch = await db.AuditLogs
            .OrderByDescending(a => a.Timestamp)
            .Skip((page - 1) * pageSize)
            .Take(pageSize + 1)
            .ToListAsync(ct);
        var hasMore = batch.Count == pageSize + 1;
        return (hasMore ? batch[..pageSize] : batch, hasMore);
    }
}
