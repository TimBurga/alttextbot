using System.Net.Http.Json;
using System.Text.Json;
using AltHeroes.Core;
using Microsoft.Extensions.Logging;

namespace AltHeroes.Bot.Services;

/// <summary>
/// Calls com.atproto.repo.listRecords on the public AppView to page through
/// posts and likes without authentication.
/// Registered as a singleton; creates HttpClients via IHttpClientFactory.
/// </summary>
public sealed class ListRecordsClient(IHttpClientFactory httpClientFactory, ILogger<ListRecordsClient> logger)
{
    private HttpClient Http => httpClientFactory.CreateClient(nameof(ListRecordsClient));
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    // PDS endpoints are stable; cache them to avoid repeated plc.directory lookups.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _pdsCache = new();

    /// <summary>
    /// Returns all image-bearing and text posts for <paramref name="did"/> within the last
    /// <paramref name="windowDays"/> days, ordered newest-first.
    /// Stops paging early once records are older than the cutoff.
    /// </summary>
    public async Task<List<PostRecord>> GetPostsAsync(
        string did,
        int windowDays,
        CancellationToken ct = default)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-windowDays);
        var posts = new List<PostRecord>();
        string? cursor = null;

        var pdsUrl = await ResolvePdsAsync(did, ct);

        while (true)
        {
            var url = $"{pdsUrl}/xrpc/com.atproto.repo.listRecords" +
                      $"?repo={Uri.EscapeDataString(did)}" +
                      $"&collection=app.bsky.feed.post&limit=100" +
                      (cursor is not null ? $"&cursor={Uri.EscapeDataString(cursor)}" : "");

            ListRecordsResponse? page;
            try
            {
                page = await Http.GetFromJsonAsync<ListRecordsResponse>(url, JsonOpts, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "ListRecordsClient: Failed to fetch posts for {Did}.", did);
                break;
            }

            if (page?.Records is not { Count: > 0 }) break;

            var reachedCutoff = false;
            foreach (var rec in page.Records)
            {
                if (!DateTimeOffset.TryParse(rec.Value.GetProperty("createdAt").GetString(), out var createdAt))
                    continue;
                if (createdAt < cutoff) { reachedCutoff = true; break; }

                posts.Add(ParsePost($"at://{did}/app.bsky.feed.post/{rec.Rkey}", createdAt, rec.Value));
            }

            if (reachedCutoff || string.IsNullOrEmpty(page.Cursor)) break;
            cursor = page.Cursor;
        }

        return posts;
    }

    private static PostRecord ParsePost(string atUri, DateTimeOffset createdAt, JsonElement value)
    {
        var images = new List<ImageRecord>();

        if (value.TryGetProperty("embed", out var embed))
            ExtractImages(embed, images);

        return new PostRecord(atUri, createdAt, images);
    }

    private static void ExtractImages(JsonElement embed, List<ImageRecord> images)
    {
        var type = embed.TryGetProperty("$type", out var t) ? t.GetString() : null;

        if (type == "app.bsky.embed.images" && embed.TryGetProperty("images", out var imgs))
        {
            foreach (var img in imgs.EnumerateArray())
                images.Add(new ImageRecord(img.TryGetProperty("alt", out var alt) ? alt.GetString() : null));
        }
        else if (type == "app.bsky.embed.recordWithMedia" && embed.TryGetProperty("media", out var media))
        {
            ExtractImages(media, images);
        }
    }

    // ── PDS resolution ───────────────────────────────────────────────────────

    /// <summary>
    /// Resolves a DID to its PDS endpoint, with in-memory caching.
    /// did:plc → plc.directory; did:web → /.well-known/did.json on the domain.
    /// </summary>
    private async Task<string> ResolvePdsAsync(string did, CancellationToken ct)
    {
        if (_pdsCache.TryGetValue(did, out var cached))
            return cached;

        var didDocUrl = did.StartsWith("did:web:")
            ? $"https://{did["did:web:".Length..]}/.well-known/did.json"
            : $"https://plc.directory/{Uri.EscapeDataString(did)}";

        var doc = await Http.GetFromJsonAsync<JsonElement>(didDocUrl, JsonOpts, ct);

        if (doc.TryGetProperty("service", out var services))
        {
            foreach (var svc in services.EnumerateArray())
            {
                if (svc.TryGetProperty("id", out var id))
                {
                    var idStr = id.GetString() ?? "";
                    if (idStr == "#atproto_pds" || idStr.EndsWith("#atproto_pds", StringComparison.Ordinal))
                    {
                        if (svc.TryGetProperty("serviceEndpoint", out var ep))
                        {
                            var url = ep.GetString()?.TrimEnd('/')
                                      ?? throw new InvalidOperationException("Empty PDS endpoint.");
                            _pdsCache[did] = url;
                            return url;
                        }
                    }
                }
            }
        }

        throw new InvalidOperationException($"No #atproto_pds service found in DID document for {did}.");
    }

    // ── Response shapes ──────────────────────────────────────────────────────

    private record ListRecordsResponse(List<RecordItem>? Records, string? Cursor);
    private record RecordItem(string Rkey, JsonElement Value);
}
