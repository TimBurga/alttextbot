using System.Net.Http.Json;
using AltTextBot.Application.Interfaces;

namespace AltTextBot.Infrastructure.Services;

public class TapApiClient(HttpClient httpClient) : ITapApiClient
{
    public async Task AddReposAsync(IEnumerable<string> dids, CancellationToken ct = default)
    {
        var response = await httpClient.PostAsJsonAsync("/repos/add", new { dids }, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task RemoveReposAsync(IEnumerable<string> dids, CancellationToken ct = default)
    {
        var response = await httpClient.PostAsJsonAsync("/repos/remove", new { dids }, ct);
        response.EnsureSuccessStatusCode();
    }
}
