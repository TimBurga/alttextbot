using AltTextBot.Domain.Enums;

namespace AltTextBot.Application.Interfaces;

public record ScoringWindow(
    int TotalImagePosts,
    int PostsWithAllAlt,
    double ScorePercent,
    LabelTier Tier);

public interface IScoringService
{
    Task<ScoringWindow> ComputeScoreAsync(string subscriberDid, CancellationToken ct = default);
}
