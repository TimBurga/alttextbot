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

    /// <summary>
    /// Finds the rkey of the like record in <paramref name="likerDid"/>'s repo
    /// that points at the labeler's service record.
    /// </summary>
    public async Task<string?> GetLikeRkeyAsync(
        string likerDid,
        string labelerDid,
        CancellationToken ct = default)
    {
        var labelerUri = $"at://{labelerDid}/app.bsky.labeler.service/self";
        string? cursor = null;

        string pdsUrl;
        try { pdsUrl = await ResolvePdsAsync(likerDid, ct); }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "ListRecordsClient: Failed to resolve PDS for {Did}.", likerDid);
            return null;
        }

        while (true)
        {
            var url = $"{pdsUrl}/xrpc/com.atproto.repo.listRecords" +
                      $"?repo={Uri.EscapeDataString(likerDid)}" +
                      $"&collection=app.bsky.feed.like&limit=100" +
                      (cursor is not null ? $"&cursor={Uri.EscapeDataString(cursor)}" : "");

            ListRecordsResponse? page;
            try
            {
                page = await Http.GetFromJsonAsync<ListRecordsResponse>(url, JsonOpts, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "ListRecordsClient: Failed to fetch likes page for {Did}.", likerDid);
                return null;
            }

            if (page?.Records is not { Count: > 0 }) return null;

            foreach (var rec in page.Records)
            {
                if (rec.Value.TryGetProperty("subject", out var subject) &&
                    subject.TryGetProperty("uri", out var uriEl) &&
                    uriEl.GetString() == labelerUri)
                {
                    return rec.Rkey;
                }
            }

            if (string.IsNullOrEmpty(page.Cursor)) return null;
            cursor = page.Cursor;
        }
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

    private async Task<string> ResolvePdsAsync(string did, CancellationToken ct)
    {
        if (_pdsCache.TryGetValue(did, out var cached))
            return cached;

        var url = await DidResolver.ResolvePdsAsync(did, Http, ct);
        _pdsCache[did] = url;
        return url;
    }

    // ── Response shapes ──────────────────────────────────────────────────────

    private record ListRecordsResponse(List<RecordItem>? Records, string? Cursor);
    private record RecordItem(string Rkey, JsonElement Value);
}
