using AltTextBot.Domain.Enums;

namespace AltTextBot.Domain.Entities;

public class Subscriber
{
    public string Did { get; private set; } = default!;
    public string Handle { get; private set; } = default!;
    public DateTimeOffset SubscribedAt { get; private set; }
    public SubscriberStatus Status { get; private set; }
    public DateTimeOffset? LastScoredAt { get; private set; }
    public ICollection<TrackedPost> TrackedPosts { get; private set; } = new List<TrackedPost>();

    private Subscriber() { }

    public static Subscriber Create(string did, string handle)
    {
        return new Subscriber
        {
            Did = did,
            Handle = handle,
            SubscribedAt = DateTimeOffset.UtcNow,
            Status = SubscriberStatus.Active
        };
    }

    public void UpdateHandle(string handle) => Handle = handle;

    public void Deactivate() => Status = SubscriberStatus.Deactivated;

    public void Reactivate() => Status = SubscriberStatus.Active;

    public void RecordScored() => LastScoredAt = DateTimeOffset.UtcNow;
}
