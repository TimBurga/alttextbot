using AltTextBot.Application.Interfaces;
using AltTextBot.Domain.Entities;
using AltTextBot.Domain.Enums;
using AltTextBot.Infrastructure.Data;

namespace AltTextBot.Infrastructure.Services;

public class AuditLogger(AltTextBotDbContext db) : IAuditLogger
{
    public async Task LogAsync(AuditEventType eventType, string? subscriberDid = null, string details = "", CancellationToken ct = default)
    {
        var entry = AuditLog.Create(eventType, subscriberDid, details);
        db.AuditLogs.Add(entry);
        await db.SaveChangesAsync(ct);
    }
}
