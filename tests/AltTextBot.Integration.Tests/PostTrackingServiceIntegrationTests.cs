using AltTextBot.Infrastructure.Services;

namespace AltTextBot.Integration.Tests;

public class PostTrackingServiceIntegrationTests(DatabaseFixture db) : IClassFixture<DatabaseFixture>
{
    private const string Did = "did:plc:posttracking-test";
    private const string AtUri = "at://did:plc:posttracking-test/app.bsky.feed.post/abc123";

    [Fact]
    public async Task RecordPostAsync_IsIdempotent()
    {
        await using var context = db.CreateDbContext();
        context.Subscribers.Add(AltTextBot.Domain.Entities.Subscriber.Create(Did, "tracking.test"));
        await context.SaveChangesAsync();

        var service = new PostTrackingService(context);

        // Record the same post twice
        await service.RecordPostAsync(AtUri, Did, null, true, 2, 2, []);
        await service.RecordPostAsync(AtUri, Did, null, true, 2, 2, []);

        var count = context.TrackedPosts.Count(p => p.AtUri == AtUri);
        count.Should().Be(1, "duplicate posts should be silently ignored");
    }

    [Fact]
    public async Task DeleteOldPostsAsync_RemovesOnlyExpiredPosts()
    {
        var did = "did:plc:delete-test";
        var oldUri = "at://did:plc:delete-test/app.bsky.feed.post/old";
        var newUri = "at://did:plc:delete-test/app.bsky.feed.post/new";

        await using var context = db.CreateDbContext();
        context.Subscribers.Add(AltTextBot.Domain.Entities.Subscriber.Create(did, "delete.test"));

        var oldPost = AltTextBot.Domain.Entities.TrackedPost.Create(oldUri, did, null, true, 1, 0);
        oldPost.CreatedAt = DateTimeOffset.UtcNow.AddDays(-40);

        var newPost = AltTextBot.Domain.Entities.TrackedPost.Create(newUri, did, null, true, 1, 1);

        context.TrackedPosts.AddRange(oldPost, newPost);
        await context.SaveChangesAsync();

        var service = new PostTrackingService(context);
        var cutoff = DateTimeOffset.UtcNow.AddDays(-30);
        await service.DeleteOldPostsAsync(cutoff);

        context.TrackedPosts.Any(p => p.AtUri == oldUri).Should().BeFalse("old post should have been deleted");
        context.TrackedPosts.Any(p => p.AtUri == newUri).Should().BeTrue("recent post should remain");
    }

    [Fact]
    public async Task DeleteOldPostsAsync_CascadesDeleteToImages()
    {
        var did = "did:plc:cascade-delete-test";
        var postUri = "at://did:plc:cascade-delete-test/app.bsky.feed.post/img001";

        await using var context = db.CreateDbContext();
        context.Subscribers.Add(AltTextBot.Domain.Entities.Subscriber.Create(did, "cascade.test"));

        var post = AltTextBot.Domain.Entities.TrackedPost.Create(postUri, did, null, true, 2, 1);
        post.CreatedAt = DateTimeOffset.UtcNow.AddDays(-40);
        context.TrackedPosts.Add(post);

        context.TrackedImages.Add(AltTextBot.Domain.Entities.TrackedImage.Create(postUri, 0, "bafybeiabc000", true));
        context.TrackedImages.Add(AltTextBot.Domain.Entities.TrackedImage.Create(postUri, 1, "bafybeiabc001", false));
        await context.SaveChangesAsync();

        var service = new PostTrackingService(context);
        await service.DeleteOldPostsAsync(DateTimeOffset.UtcNow.AddDays(-30));

        await using var verify = db.CreateDbContext();
        verify.TrackedPosts.Any(p => p.AtUri == postUri).Should().BeFalse();
        verify.TrackedImages.Any(i => i.PostAtUri == postUri).Should().BeFalse("images should be cascade-deleted with their post");
    }
}
