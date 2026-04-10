using System.Net.Http.Json;
using AltHeroes.Web.Configuration;
using Microsoft.Extensions.Options;

namespace AltHeroes.Web.Services;

/// <summary>Proxies calls to the AltHeroes.Bot admin endpoints, injecting the API key server-side.</summary>
public sealed class BotAdminClient(HttpClient http, IOptions<BotClientOptions> options)
{
    private readonly BotClientOptions _opts = options.Value;

    public async Task<SubscribersResponse?> GetSubscribersAsync(CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "/admin/subscribers");
        req.Headers.Add("X-Admin-Key", _opts.AdminApiKey);
        var resp = await http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<SubscribersResponse>(cancellationToken: ct);
    }

    public async Task<bool> BlockAsync(string did, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"/admin/block/{Uri.EscapeDataString(did)}");
        req.Headers.Add("X-Admin-Key", _opts.AdminApiKey);
        var resp = await http.SendAsync(req, ct);
        return resp.IsSuccessStatusCode;
    }

    public async Task<bool> UnblockAsync(string did, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Delete, $"/admin/block/{Uri.EscapeDataString(did)}");
        req.Headers.Add("X-Admin-Key", _opts.AdminApiKey);
        var resp = await http.SendAsync(req, ct);
        return resp.IsSuccessStatusCode;
    }

    public async Task<RescoreResult?> RescoreAsync(string did, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"/admin/rescore/{Uri.EscapeDataString(did)}");
        req.Headers.Add("X-Admin-Key", _opts.AdminApiKey);
        var resp = await http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) return null;
        return await resp.Content.ReadFromJsonAsync<RescoreResult>(cancellationToken: ct);
    }
}

public record SubscribersResponse(int Count, List<SubscriberItem> Items);
public record SubscriberItem(string Did, string Tier, bool Blocked);
public record RescoreResult(string Did, string Tier, double Score);
