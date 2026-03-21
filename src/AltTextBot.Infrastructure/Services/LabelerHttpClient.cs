using AltTextBot.Application.Interfaces;
using AltTextBot.Domain.Enums;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;

namespace AltTextBot.Infrastructure.Services;

public class FakeLabelerClient(ILogger<FakeLabelerClient> logger) : ILabelerClient
{
    public Task ApplyLabelAsync(string did, LabelTier tier, CancellationToken ct = default)
    {
        logger.LogInformation("Pretending to apply label {Label} to {Did}", tier, did);
        return Task.CompletedTask;
    }

    public Task NegateLabelAsync(string did, LabelTier tier, CancellationToken ct = default)
    {
        logger.LogInformation("Pretending to negate label {Label} for {Did}", tier, did);
        return Task.CompletedTask;
    }
}

public class LabelerHttpClient(HttpClient httpClient, ILogger<LabelerHttpClient> logger) : ILabelerClient
{
    public async Task ApplyLabelAsync(string did, LabelTier tier, CancellationToken ct = default)
    {
        logger.LogInformation("Applying label {Label} to {Did}", tier, did);
        var response = await httpClient.PostAsJsonAsync("/labels", new { did, val = tier.ToString(), neg = false }, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task NegateLabelAsync(string did, LabelTier tier, CancellationToken ct = default)
    {
        logger.LogInformation("Negating label {Label} for {Did}", tier, did);
        var response = await httpClient.PostAsJsonAsync("/labels", new { did, val = tier.ToString(), neg = true }, ct);
        response.EnsureSuccessStatusCode();
    }
}
