using AltTextBot.Application.Configuration;
using AltTextBot.Domain.Enums;
using AltTextBot.Infrastructure.Data;
using AltTextBot.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AltTextBot.Application.Tests;

public class ScoringServiceTests
{
    private static AltTextBotDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AltTextBotDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AltTextBotDbContext(options);
    }

    private static ScoringService CreateService(AltTextBotDbContext db)
    {
        var options = Options.Create(new ScoringOptions
        {
            HeroThreshold = 95.0,
            GoldThreshold = 80.0,
            SilverThreshold = 60.0,
            BronzeThreshold = 40.0,
            RollingWindowDays = 30
        });
        return new ScoringService(db, options);
    }

    [Theory]
    [InlineData(100, 100, LabelTier.Hero)]       // 100% → Hero
    [InlineData(80, 100, LabelTier.Gold)]        // 80% → Gold
    [InlineData(60, 100, LabelTier.Silver)]      // 60% → Silver
    [InlineData(40, 100, LabelTier.Bronze)]      // 40% → Bronze
    [InlineData(39, 100, LabelTier.None)]        // <40% → None
    [InlineData(0, 0, LabelTier.None)]           // no posts → None
    public async Task ComputeScore_TierThresholds(int withAlt, int total, LabelTier expectedTier)
    {
        using var db = CreateDb();
        var did = "did:plc:tier-test";

        for (var i = 0; i < total; i++)
        {
            var allAlt = i < withAlt;
            db.TrackedPosts.Add(Domain.Entities.TrackedPost.Create(
                $"at://test/post/{i}", did, null, hasImages: true,
                imageCount: 1, altTextCount: allAlt ? 1 : 0));
        }
        await db.SaveChangesAsync();

        var score = await CreateService(db).ComputeScoreAsync(did);

        score.Tier.Should().Be(expectedTier);
    }

    [Fact]
    public async Task ComputeScore_WithNoImagePosts_ReturnsZeroScore()
    {
        using var db = CreateDb();

        // Add subscriber and non-image post
        db.Subscribers.Add(AltTextBot.Domain.Entities.Subscriber.Create("did:plc:test", "test.bsky.social"));
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var score = await service.ComputeScoreAsync("did:plc:test");

        score.TotalImagePosts.Should().Be(0);
        score.ScorePercent.Should().Be(0.0);
        score.Tier.Should().Be(LabelTier.None);
    }

    [Fact]
    public async Task ComputeScore_WithAllImagesHavingAlt_ReturnsPlatinum()
    {
        using var db = CreateDb();
        var did = "did:plc:test";

        // Add image posts all with full alt text
        for (int i = 0; i < 5; i++)
        {
            var post = AltTextBot.Domain.Entities.TrackedPost.Create($"at://test/post/{i}", did, null, true, 2, 2);
            db.TrackedPosts.Add(post);
        }
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var score = await service.ComputeScoreAsync(did);

        score.TotalImagePosts.Should().Be(5);
        score.PostsWithAllAlt.Should().Be(5);
        score.ScorePercent.Should().Be(100.0);
        score.Tier.Should().Be(LabelTier.Hero);
    }

    [Fact]
    public async Task ComputeScore_WithOnlyNonImagePosts_ReturnsNone()
    {
        using var db = CreateDb();
        var did = "did:plc:non-image-only";

        // Non-image posts (hasImages: false) are excluded from scoring entirely
        for (var i = 0; i < 10; i++)
        {
            db.TrackedPosts.Add(Domain.Entities.TrackedPost.Create(
                $"at://test/post/{i}", did, null, hasImages: false, imageCount: 0, altTextCount: 0));
        }
        await db.SaveChangesAsync();

        var score = await CreateService(db).ComputeScoreAsync(did);

        score.TotalImagePosts.Should().Be(0);
        score.Tier.Should().Be(LabelTier.None, "subscribers with no image posts should not receive a label");
    }

    [Fact]
    public async Task ComputeScore_PostJustInsideRollingWindow_IsIncluded()
    {
        using var db = CreateDb();
        var did = "did:plc:boundary-test";

        // One post at 29 days old (inside the 30-day window) and one at 31 days old (outside).
        // Verifies the window is an inclusive lower bound: >= cutoff.
        var insidePost = Domain.Entities.TrackedPost.Create("at://test/post/inside", did, null, true, 1, 1);
        insidePost.CreatedAt = DateTimeOffset.UtcNow.AddDays(-29);

        var outsidePost = Domain.Entities.TrackedPost.Create("at://test/post/outside", did, null, true, 1, 0);
        outsidePost.CreatedAt = DateTimeOffset.UtcNow.AddDays(-31);

        db.TrackedPosts.AddRange(insidePost, outsidePost);
        await db.SaveChangesAsync();

        var score = await CreateService(db).ComputeScoreAsync(did);

        score.TotalImagePosts.Should().Be(1, "only the post inside the 30-day window should be counted");
        score.PostsWithAllAlt.Should().Be(1);
        score.Tier.Should().Be(LabelTier.Hero);
    }

    [Fact]
    public async Task ComputeScore_ExcludesPostsOutsideRollingWindow()
    {
        using var db = CreateDb();
        var did = "did:plc:test";

        // Old post outside the 30-day rolling window — should be excluded
        var oldPost = AltTextBot.Domain.Entities.TrackedPost.Create("at://test/post/old", did, null, true, 1, 0);
        oldPost.CreatedAt = DateTimeOffset.UtcNow.AddDays(-31);
        db.TrackedPosts.Add(oldPost);

        // Recent post inside the window — should be counted
        var recentPost = AltTextBot.Domain.Entities.TrackedPost.Create("at://test/post/new", did, null, true, 1, 1);
        db.TrackedPosts.Add(recentPost);

        await db.SaveChangesAsync();

        var service = CreateService(db);
        var score = await service.ComputeScoreAsync(did);

        score.TotalImagePosts.Should().Be(1, "only the recent post falls within the rolling window");
        score.PostsWithAllAlt.Should().Be(1);
        score.ScorePercent.Should().Be(100.0);
    }
}
