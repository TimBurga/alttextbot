using AltTextBot.Application.Interfaces;
using AltTextBot.Domain.Entities;
using AltTextBot.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AltTextBot.Infrastructure.Services;

public class PostTrackingService(AltTextBotDbContext db) : IPostTrackingService
{
    public async Task RecordPostAsync(
        string atUri,
        string subscriberDid,
        DateTimeOffset? postedAt,
        bool hasImages,
        int imageCount,
        int altTextCount,
        IReadOnlyList<TrackedImageData> images,
        CancellationToken ct = default)
    {
        // Upsert by AT URI (idempotent)
        var existing = await db.TrackedPosts.FindAsync([atUri], ct);
        if (existing is not null) return;

        var post = TrackedPost.Create(atUri, subscriberDid, postedAt, hasImages, imageCount, altTextCount);
        db.TrackedPosts.Add(post);

        foreach (var img in images)
            db.TrackedImages.Add(TrackedImage.Create(atUri, img.Index, img.BlobCid, img.HasAlt));

        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteOldPostsAsync(DateTimeOffset cutoff, CancellationToken ct = default)
    {
        await db.TrackedPosts
            .Where(p => p.CreatedAt < cutoff)
            .ExecuteDeleteAsync(ct);
    }
}
