using AltTextBot.Application.Configuration;
using AltTextBot.Domain.Enums;
using AltTextBot.Infrastructure.Services;
using Microsoft.Extensions.Options;

namespace AltTextBot.Integration.Tests;

public class ScoringServiceIntegrationTests(DatabaseFixture db) : IClassFixture<DatabaseFixture>
{
    private static ScoringService CreateService(AltTextBot.Infrastructure.Data.AltTextBotDbContext context) =>
        new(context, Options.Create(new ScoringOptions
        {
            HeroThreshold = 95.0,
            GoldThreshold = 80.0,
            SilverThreshold = 60.0,
            BronzeThreshold = 40.0,
            RollingWindowDays = 30
        }));

    [Fact]
    public async Task ComputeScore_ExcludesPostsOutsideRollingWindow()
    {
        var did = "did:plc:scoring-window-test";
        await using var context = db.CreateDbContext();
        context.Subscribers.Add(AltTextBot.Domain.Entities.Subscriber.Create(did, "scoring.test"));

        // Old post — outside 30-day window, no alt text
        var oldPost = AltTextBot.Domain.Entities.TrackedPost.Create("at://scoring/post/old", did, null, true, 1, 0);
        oldPost.CreatedAt = DateTimeOffset.UtcNow.AddDays(-31);

        // Recent post — inside window, full alt text
        var newPost = AltTextBot.Domain.Entities.TrackedPost.Create("at://scoring/post/new", did, null, true, 1, 1);

        context.TrackedPosts.AddRange(oldPost, newPost);
        await context.SaveChangesAsync();

        var score = await CreateService(context).ComputeScoreAsync(did);

        score.TotalImagePosts.Should().Be(1, "only the recent post falls within the rolling window");
        score.PostsWithAllAlt.Should().Be(1);
        score.ScorePercent.Should().Be(100.0);
        score.Tier.Should().Be(LabelTier.Hero);
    }

    [Fact]
    public async Task ComputeScore_ReturnsCorrectTierForMixedPosts()
    {
        var did = "did:plc:scoring-mixed-test";
        await using var context = db.CreateDbContext();
        context.Subscribers.Add(AltTextBot.Domain.Entities.Subscriber.Create(did, "mixed.test"));

        // 4 posts with alt text, 1 without → 80% → Gold
        for (var i = 0; i < 4; i++)
            context.TrackedPosts.Add(AltTextBot.Domain.Entities.TrackedPost.Create($"at://mixed/post/{i}", did, null, true, 1, 1));

        context.TrackedPosts.Add(AltTextBot.Domain.Entities.TrackedPost.Create("at://mixed/post/noalt", did, null, true, 1, 0));
        await context.SaveChangesAsync();

        var score = await CreateService(context).ComputeScoreAsync(did);

        score.TotalImagePosts.Should().Be(5);
        score.PostsWithAllAlt.Should().Be(4);
        score.ScorePercent.Should().BeApproximately(80.0, 0.01);
        score.Tier.Should().Be(LabelTier.Gold);
    }
}
