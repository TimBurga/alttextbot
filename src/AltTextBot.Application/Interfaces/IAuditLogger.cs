using AltTextBot.Domain.Enums;

namespace AltTextBot.Application.Interfaces;

public interface IAuditLogger
{
    Task LogAsync(AuditEventType eventType, string? subscriberDid = null, string details = "", CancellationToken ct = default);
}
