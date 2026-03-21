namespace AltTextBot.Domain.Entities;

public class FirehoseState
{
    public int Id { get; private set; } = 1;
    public long LastTimeUs { get; private set; }

    private FirehoseState() { }

    public static FirehoseState Create() => new FirehoseState { LastTimeUs = 0 };

    public void UpdateCursor(long timeUs) => LastTimeUs = timeUs;
}
