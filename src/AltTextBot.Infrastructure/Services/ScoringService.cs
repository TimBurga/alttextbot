using AltTextBot.Application.Configuration;
using AltTextBot.Application.Interfaces;
using AltTextBot.Domain.Enums;
using AltTextBot.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AltTextBot.Infrastructure.Services;

public class ScoringService(AltTextBotDbContext db, IOptions<ScoringOptions> options) : IScoringService
{
    private readonly ScoringOptions _options = options.Value;

    public async Task<ScoringWindow> ComputeScoreAsync(string subscriberDid, CancellationToken ct = default)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-_options.RollingWindowDays);

        var result = await db.TrackedPosts
            .Where(p => p.SubscriberDid == subscriberDid && p.CreatedAt >= cutoff && p.HasImages)
            .GroupBy(_ => 1)
            .Select(g => new { Total = g.Count(), WithAllAlt = g.Count(p => p.AllImagesHaveAlt) })
            .FirstOrDefaultAsync(ct);

        var total = result?.Total ?? 0;
        var withAllAlt = result?.WithAllAlt ?? 0;
        var scorePercent = total == 0 ? 0.0 : (withAllAlt * 100.0) / total;
        var tier = DetermineTargetTier(scorePercent);

        return new ScoringWindow(total, withAllAlt, scorePercent, tier);
    }

    private LabelTier DetermineTargetTier(double scorePercent)
    {
        if (scorePercent >= _options.HeroThreshold) return LabelTier.Hero;
        if (scorePercent >= _options.GoldThreshold) return LabelTier.Gold;
        if (scorePercent >= _options.SilverThreshold) return LabelTier.Silver;
        if (scorePercent >= _options.BronzeThreshold) return LabelTier.Bronze;
        return LabelTier.None;
    }
}
