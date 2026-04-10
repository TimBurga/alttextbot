namespace AltHeroes.Core;

public static class ScoringService
{
    /// <summary>
    /// Computes a tier from a sequence of fetched posts.
    /// Text posts (Images.Count == 0) are invisible — not counted in numerator or denominator.
    /// A compliant post has ALL images with AltText.Length >= config.AltTextMinimumLength.
    /// </summary>
    public static ScoreResult ComputeTier(
        IEnumerable<PostRecord> posts,
        ScoringConfig config,
        DateTimeOffset now)
    {
        var cutoff = now.AddDays(-config.WindowDays);

        var imagePosts = posts
            .Where(p => p.CreatedAt >= cutoff && p.Images.Count > 0)
            .ToList();

        var total = imagePosts.Count;

        var compliant = imagePosts.Count(p =>
            p.Images.All(img => img.AltText?.Length >= config.AltTextMinimumLength));

        var score = total == 0 ? 0.0 : compliant * 100.0 / total;

        var tier = DetermineTier(score, total, config);

        return new ScoreResult(tier, total, compliant, score);
    }

    private static LabelTier DetermineTier(double score, int totalImagePosts, ScoringConfig config)
    {
        if (score >= config.HeroThreshold && totalImagePosts >= config.HeroMinimumImagePosts)
            return LabelTier.Hero;
        if (score >= config.GoldThreshold) return LabelTier.Gold;
        if (score >= config.SilverThreshold) return LabelTier.Silver;
        if (score >= config.BronzeThreshold) return LabelTier.Bronze;
        return LabelTier.None;
    }
}
