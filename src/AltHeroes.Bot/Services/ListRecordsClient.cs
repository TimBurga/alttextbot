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
    /// Pages through all likes on <paramref name="labelerDid"/>'s profile record,
    /// returning (likerDid, rkey) pairs for enrollment.
    /// </summary>
    public async Task<List<(string Did, string Rkey)>> GetProfileLikesAsync(
        string labelerDid,
        CancellationToken ct = default)
    {
        var results = new List<(string, string)>();
        string? cursor = null;
        // We enumerate the labeler's *own* repo listRecords for app.bsky.feed.like
        // to find who liked the profile. Because likes live in the *liker's* repo,
        // we must instead enumerate the labeler's likes collection... but actually
        // likes-on-a-profile are stored in the LIKER's repo.
        // We use app.bsky.feed.getLikes (AppView aggregation endpoint) instead.
        while (true)
        {
            var profileUri = $"at://{labelerDid}/app.bsky.actor.profile/self";
            var url = $"https://public.api.bsky.app/xrpc/app.bsky.feed.getLikes" +
                      $"?uri={Uri.EscapeDataString(profileUri)}&limit=100" +
                      (cursor is not null ? $"&cursor={Uri.EscapeDataString(cursor)}" : "");

            GetLikesResponse? page;
            try
            {
                page = await Http.GetFromJsonAsync<GetLikesResponse>(url, JsonOpts, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "ListRecordsClient: Failed to fetch likes for labeler {Did}.", labelerDid);
                break;
            }

            if (page?.Likes is not { Count: > 0 }) break;

            foreach (var like in page.Likes)
            {
                // getLikes returns actor info but not the rkey. We need the rkey to
                // populate _likeRkeyIndex for unenroll-on-delete. Fetch it per-actor
                // from their repo. We do this lazily here by listing their likes.
                var rkey = await GetLikeRkeyAsync(like.Actor.Did, labelerDid, ct);
                if (rkey is not null)
                    results.Add((like.Actor.Did, rkey));
                else
                    logger.LogDebug("ListRecordsClient: Could not resolve like rkey for {Did}.", like.Actor.Did);
            }

            if (string.IsNullOrEmpty(page.Cursor)) break;
            cursor = page.Cursor;
        }

        return results;
    }

    /// <summary>
    /// Finds the rkey of the like record in <paramref name="likerDid"/>'s repo
    /// that points at the labeler's profile.
    /// </summary>
    public async Task<string?> GetLikeRkeyAsync(
        string likerDid,
        string labelerDid,
        CancellationToken ct = default)
    {
        var profileUri = $"at://{labelerDid}/app.bsky.actor.profile/self";
        string? cursor = null;

        string pdsUrl;
        try { pdsUrl = await ResolvePdsAsync(likerDid, ct); }
        catch { return null; }

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
            catch
            {
                return null;
            }

            if (page?.Records is not { Count: > 0 }) return null;

            foreach (var rec in page.Records)
            {
                if (rec.Value.TryGetProperty("subject", out var subject) &&
                    subject.TryGetProperty("uri", out var uriEl) &&
                    uriEl.GetString() == profileUri)
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
    private record GetLikesResponse(List<LikeItem>? Likes, string? Cursor);
    private record LikeItem(ActorItem Actor);
    private record ActorItem(string Did);
}
