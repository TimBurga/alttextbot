using AltTextBot.Application.Interfaces;
using AltTextBot.Domain.Entities;
using AltTextBot.Domain.Enums;
using AltTextBot.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AltTextBot.Infrastructure.Services;

public class SubscriberService(
    AltTextBotDbContext db,
    ISubscriberSet subscriberSet,
    IAuditLogger auditLogger,
    ITapApiClient tapApiClient,
    ILogger<SubscriberService> logger) : ISubscriberService
{
    public async Task SubscribeAsync(string did, string handle, CancellationToken ct = default)
    {
        var existing = await db.Subscribers.FindAsync([did], ct);
        if (existing is not null)
        {
            if (existing.Status == SubscriberStatus.Deactivated)
            {
                existing.Reactivate();
                existing.UpdateHandle(handle);
                await db.SaveChangesAsync(ct);
                subscriberSet.Add(did);
                await auditLogger.LogAsync(AuditEventType.SubscriberReactivated, did, $"Reactivated via like. Handle: {handle}", ct);
                logger.LogInformation("Reactivated subscriber {Did}", did);
                await RegisterWithTapAsync([did], ct);
            }
            else
            {
                logger.LogDebug("Subscriber {Did} is already active, skipping duplicate subscribe.", did);
            }
            return;
        }

        var subscriber = Subscriber.Create(did, handle);
        db.Subscribers.Add(subscriber);
        await db.SaveChangesAsync(ct);
        subscriberSet.Add(did);
        await auditLogger.LogAsync(AuditEventType.SubscriberAdded, did, $"New subscriber via like. Handle: {handle}", ct);
        logger.LogInformation("New subscriber {Did} ({Handle})", did, handle);
        await RegisterWithTapAsync([did], ct);
    }

    public async Task UnsubscribeAsync(string did, CancellationToken ct = default)
    {
        var existing = await db.Subscribers.FindAsync([did], ct);
        if (existing is null || existing.Status == SubscriberStatus.Deactivated) return;

        existing.Deactivate();
        await db.SaveChangesAsync(ct);
        subscriberSet.Remove(did);
        await auditLogger.LogAsync(AuditEventType.SubscriberDeactivated, did, "Deactivated via unlike.", ct);
        logger.LogInformation("Deactivated subscriber {Did}", did);
        await DeregisterWithTapAsync([did], ct);
    }

    public async Task ReactivateAsync(string did, CancellationToken ct = default)
    {
        var existing = await db.Subscribers.FindAsync([did], ct);
        if (existing is null) return;

        existing.Reactivate();
        await db.SaveChangesAsync(ct);
        subscriberSet.Add(did);
        await auditLogger.LogAsync(AuditEventType.SubscriberReactivated, did, "Reactivated via admin.", ct);
        await RegisterWithTapAsync([did], ct);
    }

    public async Task<IReadOnlyList<string>> GetActiveSubscriberDidsAsync(CancellationToken ct = default)
    {
        return await db.Subscribers
            .Where(s => s.Status == SubscriberStatus.Active)
            .Select(s => s.Did)
            .ToListAsync(ct);
    }

    private async Task RegisterWithTapAsync(IEnumerable<string> dids, CancellationToken ct)
    {
        try
        {
            await tapApiClient.AddReposAsync(dids, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to register DIDs with Tap. Events for these repos will not be received until Tap reconnects.");
        }
    }

    private async Task DeregisterWithTapAsync(IEnumerable<string> dids, CancellationToken ct)
    {
        try
        {
            await tapApiClient.RemoveReposAsync(dids, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to deregister DIDs from Tap.");
        }
    }
}
