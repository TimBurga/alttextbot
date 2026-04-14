using System.Text;
using System.Text.Json;
using AltHeroes.Bot.Configuration;
using Microsoft.Extensions.Options;

namespace AltHeroes.Bot.Services;

/// <summary>
/// Registered last so its StopAsync runs after all other services have stopped.
/// Sends a Discord webhook notification only when a BackgroundService faulted,
/// distinguishing an unexpected crash from a normal graceful shutdown.
/// </summary>
public sealed class ShutdownNotifierService(
    IEnumerable<IHostedService> hostedServices,
    IHttpClientFactory httpClientFactory,
    IOptions<DiscordOptions> discordOptions,
    ILogger<ShutdownNotifierService> logger) : IHostedService
{
    private readonly DiscordOptions _discord = discordOptions.Value;

    public Task StartAsync(CancellationToken ct) => Task.CompletedTask;

    public async Task StopAsync(CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_discord.WebhookUrl))
        {
            logger.LogDebug("ShutdownNotifierService: No webhook URL configured — skipping.");
            return;
        }

        var faulted = hostedServices
            .OfType<BackgroundService>()
            .Where(s => s.ExecuteTask?.IsFaulted == true)
            .ToList();

        if (faulted.Count == 0) return; // graceful shutdown — no notification needed

        var names = string.Join(", ", faulted.Select(s => s.GetType().Name));
        logger.LogWarning("ShutdownNotifierService: Faulted service(s): {Services}. Sending Discord notification.", names);

        try
        {
            var message = $"⚠️ **AltHeroes bot shut down unexpectedly.**\nFaulted service(s): `{names}`";
            var payload = JsonSerializer.Serialize(new { content = message });

            using var client = httpClientFactory.CreateClient(nameof(ShutdownNotifierService));
            using var body = new StringContent(payload, Encoding.UTF8, "application/json");
            var response = await client.PostAsync(_discord.WebhookUrl, body, ct);

            if (response.IsSuccessStatusCode)
                logger.LogInformation("ShutdownNotifierService: Discord notification sent.");
            else
                logger.LogWarning("ShutdownNotifierService: Discord returned {Status}.", response.StatusCode);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "ShutdownNotifierService: Failed to send Discord notification.");
        }
    }
}
