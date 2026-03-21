using AltTextBot.Application.Interfaces;
using AltTextBot.Domain.Enums;
using AltTextBot.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AltTextBot.Infrastructure.Services;

public class LabelStateReader(LabelerDbContext db) : ILabelStateReader
{
    private static readonly Dictionary<string, LabelTier> TierMap = new()
    {
        [nameof(LabelTier.Bronze)] = LabelTier.Bronze,
        [nameof(LabelTier.Silver)] = LabelTier.Silver,
        [nameof(LabelTier.Gold)] = LabelTier.Gold,
        [nameof(LabelTier.Hero)] = LabelTier.Hero,
    };

    // Separate HashSet so EF Core can translate .Contains() to SQL IN (...)
    private static readonly HashSet<string> KnownLabels = [..TierMap.Keys];

    public async Task<LabelTier?> GetCurrentLabelAsync(string did, CancellationToken ct = default)
    {
        var labels = await db.Labels
            .Where(l => l.Did == did && !l.Neg && KnownLabels.Contains(l.Val))
            .ToListAsync(ct);

        // Find the highest active tier
        LabelTier? highest = null;
        foreach (var label in labels)
        {
            if (TierMap.TryGetValue(label.Val, out var tier))
            {
                if (highest is null || tier > highest)
                    highest = tier;
            }
        }
        return highest;
    }
}
