namespace AltTextBot.Application.Interfaces;

public record TrackedImageData(int Index, string BlobCid, bool HasAlt);

public interface IPostTrackingService
{
    Task RecordPostAsync(
        string atUri,
        string subscriberDid,
        DateTimeOffset? postedAt,
        bool hasImages,
        int imageCount,
        int altTextCount,
        IReadOnlyList<TrackedImageData> images,
        CancellationToken ct = default);

    Task DeleteOldPostsAsync(DateTimeOffset cutoff, CancellationToken ct = default);
}
