using AltHeroes.Bot.Configuration;
using AltHeroes.Core;
using idunno.Bluesky;
using Microsoft.Extensions.Options;

namespace AltHeroes.Bot.Services;

/// <summary>Posts congratulations on Bluesky when a subscriber upgrades tiers.</summary>
public sealed class CongratsPostService(
    IOptions<BotOptions> botOptions,
    ILogger<CongratsPostService> logger) : ICongratsPostService, IDisposable
{
    private readonly BotOptions _bot = botOptions.Value;
    private readonly SemaphoreSlim _loginLock = new(1, 1);
    private BlueskyAgent? _agent;

    public async Task PostCongratsAsync(string did, string handle, LabelTier newTier, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_bot.AppPassword))
        {
            logger.LogDebug("CongratsPostService: AppPassword not configured — skipping congrats post.");
            return;
        }

        var agent = await GetAgentAsync(ct);
        if (agent is null) return;

        var (emoji, tierName) = newTier switch
        {
            LabelTier.Bronze => ("🥉", "Bronze"),
            LabelTier.Silver => ("🥈", "Silver"),
            LabelTier.Gold => ("🥇", "Gold"),
            LabelTier.Hero => ("👑", "Hero"),
            _ => ("", "")
        };

        if (string.IsNullOrEmpty(emoji)) return;

        var displayHandle = handle.StartsWith("did:") ? "you" : $"@{handle}";
        var text = $"{displayHandle} just earned the {emoji} {tierName} AltHero label! " +
                   $"Congratulations and thank you for adding alt text to your images.\r\n\r\n" +
                   $"~ Like the AltHeroes profile to track your own score! ~";

        try
        {
            var result = await agent.Post(text, cancellationToken: ct);
            if (result.Succeeded)
                logger.LogInformation("CongratsPostService: Posted congrats to {Handle} for {Tier}.", handle, tierName);
            else
                logger.LogWarning("CongratsPostService: Post failed for {Did} ({Tier}): {Error}",
                    did, tierName, result.AtErrorDetail?.Message);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "CongratsPostService: Failed to post congrats for {Did}.", did);
        }
    }

    private async Task<BlueskyAgent?> GetAgentAsync(CancellationToken ct)
    {
        if (_agent is not null) return _agent;

        await _loginLock.WaitAsync(ct);
        try
        {
            if (_agent is not null) return _agent;

            var agent = new BlueskyAgent();
            var result = await agent.Login(_bot.Handle, _bot.AppPassword, cancellationToken: ct);
            if (!result.Succeeded)
            {
                logger.LogWarning("CongratsPostService: Login failed: {Error}", result.AtErrorDetail?.Message);
                agent.Dispose();
                return null;
            }

            _agent = agent;
            logger.LogInformation("CongratsPostService: Authenticated as {Handle}.", _bot.Handle);
            return _agent;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "CongratsPostService: Could not authenticate.");
            return null;
        }
        finally { _loginLock.Release(); }
    }

    public void Dispose()
    {
        _agent?.Dispose();
        _loginLock.Dispose();
    }
}
