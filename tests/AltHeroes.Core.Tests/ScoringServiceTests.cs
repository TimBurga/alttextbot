using AltHeroes.Core;
using FluentAssertions;
using Xunit;

namespace AltHeroes.Core.Tests;

public class ScoringServiceTests
{
    private static readonly ScoringConfig Config = new();
    private static readonly DateTimeOffset Now = new(2025, 1, 15, 12, 0, 0, TimeSpan.Zero);

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Creates a post with one image whose alt text is <paramref name="alt"/>.</summary>
    private static PostRecord Post(string? alt, int daysAgo = 1) =>
        new($"at://did:example/app.bsky.feed.post/{daysAgo}",
            Now.AddDays(-daysAgo),
            [new ImageRecord(alt)]);

    private static PostRecord TextOnlyPost(int daysAgo = 1) =>
        new($"at://did:example/app.bsky.feed.post/text{daysAgo}",
            Now.AddDays(-daysAgo),
            []);

    private static PostRecord MultiImagePost(params string?[] alts) =>
        new("at://did:example/app.bsky.feed.post/multi",
            Now.AddDays(-1),
            alts.Select(a => new ImageRecord(a)).ToList());

    private static ScoreResult Score(IEnumerable<PostRecord> posts) =>
        ScoringService.ComputeTier(posts, Config, Now);

    // ── No posts ──────────────────────────────────────────────────────────────

    [Fact]
    public void NoPosts_ReturnsNone()
    {
        Score([]).Tier.Should().Be(LabelTier.None);
    }

    [Fact]
    public void TextOnlyPosts_AreInvisible_ReturnsNone()
    {
        Score([TextOnlyPost(), TextOnlyPost(2), TextOnlyPost(3)]).Tier.Should().Be(LabelTier.None);
    }

    // ── Rolling window ────────────────────────────────────────────────────────

    [Fact]
    public void PostsOlderThanWindow_AreExcluded()
    {
        var oldPost = Post("good alt text", daysAgo: 31);
        Score([oldPost]).TotalImagePosts.Should().Be(0);
    }

    [Fact]
    public void PostExactlyAtCutoff_IsIncluded()
    {
        // 30 days ago exactly is still within the window
        var post = Post("good alt text", daysAgo: 30);
        Score([post]).TotalImagePosts.Should().Be(1);
    }

    // ── Alt text minimum length ───────────────────────────────────────────────

    [Fact]
    public void AltText_ExactlyMinimumLength_IsCompliant()
    {
        // minimum is 5 chars
        var post = Post("hello"); // 5 chars — compliant
        var result = Score([post]);
        result.CompliantPosts.Should().Be(1);
        // 100% compliance but only 1 image post — heroMinimumImagePosts (3) not met → Gold
        result.Tier.Should().Be(LabelTier.Gold);
    }

    [Fact]
    public void AltText_OneLessThanMinimum_IsNotCompliant()
    {
        var post = Post("hi!!"); // 4 chars — below minimum
        var result = Score([post]);
        result.CompliantPosts.Should().Be(0);
        result.Tier.Should().Be(LabelTier.None); // 0% → None
    }

    [Fact]
    public void NullAltText_IsNotCompliant()
    {
        Score([Post(null)]).CompliantPosts.Should().Be(0);
    }

    [Fact]
    public void EmptyAltText_IsNotCompliant()
    {
        Score([Post("")]).CompliantPosts.Should().Be(0);
    }

    // ── Multi-image posts ─────────────────────────────────────────────────────

    [Fact]
    public void AllImagesHaveAlt_PostIsCompliant()
    {
        var post = MultiImagePost("first alt text", "second alt text");
        Score([post]).CompliantPosts.Should().Be(1);
    }

    [Fact]
    public void OneImageMissingAlt_PostIsNotCompliant()
    {
        var post = MultiImagePost("good alt text here", null);
        Score([post]).CompliantPosts.Should().Be(0);
    }

    [Fact]
    public void OneImageShortAlt_PostIsNotCompliant()
    {
        var post = MultiImagePost("good alt text here", "hi"); // "hi" < 5 chars
        Score([post]).CompliantPosts.Should().Be(0);
    }

    // ── Tier thresholds ───────────────────────────────────────────────────────

    [Theory]
    [InlineData(0, 10, LabelTier.None)]        // 0%  → None   (< 70%)
    [InlineData(5, 10, LabelTier.None)]        // 50% → None   (< 70%)
    [InlineData(6, 10, LabelTier.None)]        // 60% → None   (< 70%)
    [InlineData(7, 10, LabelTier.Bronze)]      // 70% → Bronze (>= 70%, < 85%)
    [InlineData(8, 10, LabelTier.Bronze)]      // 80% → Bronze (>= 70%, < 85%)
    [InlineData(9, 10, LabelTier.Silver)]      // 90% → Silver (>= 85%, < 95%)
    public void TierThresholds_AreCorrect(int compliant, int total, LabelTier expected)
    {
        var posts = Enumerable.Range(0, total).Select(i =>
            i < compliant
                ? Post("good alt text", i + 1)
                : Post(null, i + 1)
        );
        Score(posts).Tier.Should().Be(expected);
    }

    // ── Hero minimum post count ───────────────────────────────────────────────

    [Fact]
    public void Hero_RequiresMinimumImagePosts()
    {
        // 100% compliant but only 2 posts (heroMinimum = 3) → Gold
        var posts = new[] { Post("good alt text", 1), Post("good alt text", 2) };
        Score(posts).Tier.Should().Be(LabelTier.Gold);
    }

    [Fact]
    public void Hero_GrantedWhenMinimumPostsMet()
    {
        var posts = Enumerable.Range(1, 5).Select(i => Post("good alt text here", i));
        // 100% compliant, 5 posts (>= heroMinimum 3) → Hero
        Score(posts).Tier.Should().Be(LabelTier.Hero);
    }

    [Fact]
    public void Gold_At95Percent_With20Posts()
    {
        // 19/20 = 95% exactly, 20 posts → Gold (not Hero, requires 100%)
        var posts = Enumerable.Range(1, 20).Select(i =>
            i == 20 ? Post(null, i) : Post("good alt text", i));
        Score(posts).Tier.Should().Be(LabelTier.Gold);
    }

    // ── Score / count accuracy ────────────────────────────────────────────────

    [Fact]
    public void Score_IsCalculatedCorrectly()
    {
        // 3 compliant out of 4 = 75%
        var posts = new[]
        {
            Post("good alt text", 1),
            Post("good alt text", 2),
            Post("good alt text", 3),
            Post(null, 4)
        };
        var result = Score(posts);
        result.TotalImagePosts.Should().Be(4);
        result.CompliantPosts.Should().Be(3);
        result.Score.Should().BeApproximately(75.0, 0.01);
        result.Tier.Should().Be(LabelTier.Bronze);
    }

    [Fact]
    public void TextPostsDoNotAffectDenominator()
    {
        // 3 image posts (all compliant) + 10 text posts = 100% compliance, not 3/13
        var posts = Enumerable.Range(1, 3).Select(i => Post("good alt text", i))
            .Concat(Enumerable.Range(10, 10).Select(i => TextOnlyPost(i)));
        var result = Score(posts);
        result.TotalImagePosts.Should().Be(3);
        result.Score.Should().BeApproximately(100.0, 0.01);
    }
}
