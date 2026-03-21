using AltTextBot.Domain.Enums;

namespace AltTextBot.Domain.Entities;

public class AuditLog
{
    public long Id { get; private set; }
    public DateTimeOffset Timestamp { get; private set; }
    public string? SubscriberDid { get; private set; }
    public AuditEventType EventType { get; private set; }
    public string Details { get; private set; } = default!;

    private AuditLog() { }

    public static AuditLog Create(AuditEventType eventType, string? subscriberDid, string details)
    {
        return new AuditLog
        {
            Timestamp = DateTimeOffset.UtcNow,
            SubscriberDid = subscriberDid,
            EventType = eventType,
            Details = details
        };
    }
}
