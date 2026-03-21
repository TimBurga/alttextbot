namespace AltTextBot.Domain.Entities;

public class TrackedPost
{
    private readonly List<TrackedImage> _images = [];

    public string AtUri { get; private set; } = default!;
    public string SubscriberDid { get; private set; } = default!;
    public DateTimeOffset CreatedAt { get; internal set; }
    public DateTimeOffset? PostedAt { get; private set; }
    public bool HasImages { get; private set; }
    public int ImageCount { get; private set; }
    public int AltTextCount { get; private set; }
    public bool AllImagesHaveAlt { get; private set; }

    // Navigation
    public Subscriber? Subscriber { get; private set; }
    public IReadOnlyList<TrackedImage> Images => _images;

    private TrackedPost() { }

    public static TrackedPost Create(
        string atUri,
        string subscriberDid,
        DateTimeOffset? postedAt,
        bool hasImages,
        int imageCount,
        int altTextCount)
    {
        return new TrackedPost
        {
            AtUri = atUri,
            SubscriberDid = subscriberDid,
            CreatedAt = DateTimeOffset.UtcNow,
            PostedAt = postedAt,
            HasImages = hasImages,
            ImageCount = imageCount,
            AltTextCount = altTextCount,
            AllImagesHaveAlt = hasImages && imageCount > 0 && altTextCount >= imageCount
        };
    }
}
