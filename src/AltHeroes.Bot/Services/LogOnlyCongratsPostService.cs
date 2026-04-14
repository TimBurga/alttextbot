using AltHeroes.Core;

namespace AltHeroes.Bot.Services;

/// <summary>
/// Debug-mode implementation that logs congrats instead of posting to Bluesky.
/// </summary>
public sealed class LogOnlyCongratsPostService(ILogger<LogOnlyCongratsPostService> logger) : ICongratsPostService
{
    public Task PostCongratsAsync(string did, string handle, LabelTier newTier, CancellationToken ct = default)
    {
        logger.LogInformation(
            "LogOnlyCongratsPostService: Would post congrats to {Handle} ({Did}) for {Tier}.",
            handle, did, newTier);
        return Task.CompletedTask;
    }
}
