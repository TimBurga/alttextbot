using AltHeroes.Core;

namespace AltHeroes.Bot.Services;

/// <summary>Posts congratulations when a subscriber upgrades tiers.</summary>
public interface ICongratsPostService
{
    Task PostCongratsAsync(string did, string handle, LabelTier newTier, CancellationToken ct = default);
}
