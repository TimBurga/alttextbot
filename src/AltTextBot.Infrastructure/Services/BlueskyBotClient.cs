using AltTextBot.Application.Configuration;
using AltTextBot.Application.Interfaces;
using AltTextBot.Domain.Enums;
using idunno.Bluesky;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AltTextBot.Infrastructure.Services;

public sealed class BlueskyBotClient(
    IOptions<BotOptions> botOptions,
    ILogger<BlueskyBotClient> logger) : IBlueskyPostClient, IDisposable
{
    private readonly BotOptions _options = botOptions.Value;
    private readonly SemaphoreSlim _loginLock = new(1, 1);
    private BlueskyAgent? _agent;

    public async Task PostCongratsAsync(string did, string? handle, LabelTier newTier, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_options.AppPassword))
        {
            logger.LogWarning("BlueskyBotClient: Bot:AppPassword is not configured. Skipping congrats post for {Did}.", did);
            return;
        }

        try
        {
            var agent = await GetAuthenticatedAgentAsync(ct);
            if (agent is null) return;

            var mention = handle is not null ? $"@{handle}" : "you";

            var text = newTier switch
            {
                LabelTier.Bronze => $"Congrats {mention} — you've earned the Bronze label on Alt Heroes! You're on your way to making Bluesky more accessible. Keep adding alt text! ♿",
                LabelTier.Silver => $"Congrats {mention} — you've leveled up to Silver on Alt Heroes! Your commitment to alt text is making a real difference. ♿",
                LabelTier.Gold => $"Congrats {mention} — Gold label on Alt Heroes! You're a true alt text champion. 🏅♿",
                LabelTier.Hero => $"Congrats {mention} — you've reached Hero status on Alt Heroes! An outstanding example of accessible posting on Bluesky. 🦸♿",
                _ => null
            };

            if (text is null) return;

            var result = await agent.Post(text, cancellationToken: ct);
            if (result.Succeeded)
                logger.LogInformation("BlueskyBotClient: Posted congrats to {Did} for tier {Tier}.", did, newTier);
            else
                logger.LogWarning("BlueskyBotClient: Post failed for {Did} (tier {Tier}): {Error}", did, newTier, result.AtErrorDetail?.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "BlueskyBotClient: Error posting congrats for {Did}.", did);
        }
    }

    private async Task<BlueskyAgent?> GetAuthenticatedAgentAsync(CancellationToken ct)
    {
        if (_agent is not null) return _agent;

        await _loginLock.WaitAsync(ct);
        try
        {
            if (_agent is not null) return _agent;

            var agent = new BlueskyAgent();
            var result = await agent.Login(_options.Handle, _options.AppPassword, cancellationToken: ct);
            if (!result.Succeeded)
            {
                logger.LogError("BlueskyBotClient: Login failed: {Error}", result.AtErrorDetail?.Message);
                agent.Dispose();
                return null;
            }

            _agent = agent;
            logger.LogInformation("BlueskyBotClient: Authenticated as {Handle}.", _options.Handle);
            return _agent;
        }
        finally
        {
            _loginLock.Release();
        }
    }

    public void Dispose()
    {
        _agent?.Dispose();
        _loginLock.Dispose();
    }
}
