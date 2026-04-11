using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using AltHeroes.Core;
using AltHeroes.Web.Configuration;
using Microsoft.Extensions.Options;

namespace AltHeroes.Web.Services;

/// <summary>
/// Streams per-image scoring events for a DID by paging through listRecords
/// and emitting SSE-ready events as each post is processed.
/// </summary>
public sealed class ScoringStreamService(
    IHttpClientFactory httpClientFactory,
    IOptions<ScoringOptions> options,
    ILogger<ScoringStreamService> logger)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public async IAsyncEnumerable<ScoringEvent> StreamAsync(
        string did,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var config = options.Value.ToConfig();
        var cutoff = DateTimeOffset.UtcNow.AddDays(-config.WindowDays);
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);
        var cutoffDate = DateOnly.FromDateTime(cutoff.UtcDateTime);
        var http = httpClientFactory.CreateClient(nameof(ScoringStreamService));

        // listRecords is a PDS-level API; resolve the user's PDS from their DID document first.
        // Note: yield is not allowed inside a catch clause, so we resolve outside the iterator body.
        string? pdsUrl = null;
        try
        {
            pdsUrl = await ResolvePdsAsync(did, http, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "ScoringStreamService: Failed to resolve PDS for {Did}.", did);
        }

        if (pdsUrl is null)
        {
            yield return new DoneEvent("None", 0, 0, 0);
            yield break;
        }

        DateOnly? currentDate = null;
        var dayPosts = new List<PostRecord>();
        var totalImagePosts = 0;
        var compliantPosts = 0;

        string? cursor = null;
        var reachedCutoff = false;

        while (!reachedCutoff && !ct.IsCancellationRequested)
        {
            var url = $"{pdsUrl}/xrpc/com.atproto.repo.listRecords" +
                      $"?repo={Uri.EscapeDataString(did)}" +
                      "&collection=app.bsky.feed.post&limit=100" +
                      (cursor is not null ? $"&cursor={Uri.EscapeDataString(cursor)}" : "");

            ListRecordsResponse? page;
            try
            {
                page = await http.GetFromJsonAsync<ListRecordsResponse>(url, JsonOpts, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "ScoringStreamService: Failed to fetch posts for {Did}.", did);
                break;
            }

            if (page?.Records is not { Count: > 0 }) break;

            foreach (var rec in page.Records)
            {
                if (!DateTimeOffset.TryParse(rec.Value.GetProperty("createdAt").GetString(), out var createdAt))
                    continue;

                if (createdAt < cutoff)
                {
                    reachedCutoff = true;
                    break;
                }

                var postDate = DateOnly.FromDateTime(createdAt.UtcDateTime);
                var post = ParsePost(rec.Uri, createdAt, rec.Value);

                // Day boundary: emit day_complete for the day we just finished
                if (currentDate is not null && postDate != currentDate)
                {
                    var allCompliant = dayPosts.Count > 0 &&
                        dayPosts.All(p => p.Images.All(img => img.AltText?.Length >= config.AltTextMinimumLength));
                    var postInfos = BuildPostInfos(dayPosts, config.AltTextMinimumLength);
                    yield return new DayCompleteEvent(currentDate.Value.ToString("yyyy-MM-dd"), allCompliant, postInfos);
                    // Brief pause so the star pop feels weighted before moving to the next day
                    await Task.Delay(180, ct);
                    dayPosts.Clear();

                    // Fill empty days between the day just completed and the new post date
                    for (var d = currentDate.Value.AddDays(-1); d > postDate; d = d.AddDays(-1))
                    {
                        yield return new DayCompleteEvent(d.ToString("yyyy-MM-dd"), false, []);
                        await Task.Delay(40, ct);
                    }
                }
                else if (currentDate is null)
                {
                    // First post seen: fill gap from today down to this post's date
                    for (var d = today; d > postDate; d = d.AddDays(-1))
                    {
                        yield return new DayCompleteEvent(d.ToString("yyyy-MM-dd"), false, []);
                        await Task.Delay(40, ct);
                    }
                }

                currentDate = postDate;

                if (post.Images.Count > 0)
                {
                    dayPosts.Add(post);
                    totalImagePosts++;
                    if (post.Images.All(img => img.AltText?.Length >= config.AltTextMinimumLength))
                        compliantPosts++;

                    // One image event per image so the counter increments visually
                    foreach (var _ in post.Images)
                    {
                        yield return new ImageEvent(postDate.ToString("yyyy-MM-dd"));
                        // Paced delay so each tick is visible; cancels immediately if client disconnects
                        await Task.Delay(80, ct);
                    }
                }
            }

            if (reachedCutoff || string.IsNullOrEmpty(page.Cursor)) break;
            cursor = page.Cursor;
        }

        // Emit day_complete for the last day (even if it had no image posts)
        if (currentDate is not null)
        {
            var allCompliant = dayPosts.Count > 0 &&
                dayPosts.All(p => p.Images.All(img => img.AltText?.Length >= config.AltTextMinimumLength));
            var lastPostInfos = BuildPostInfos(dayPosts, config.AltTextMinimumLength);
            yield return new DayCompleteEvent(currentDate.Value.ToString("yyyy-MM-dd"), allCompliant, lastPostInfos);
            await Task.Delay(180, ct);

            // Fill tail: empty days from (lastDate - 1) down to the cutoff date
            for (var d = currentDate.Value.AddDays(-1); d >= cutoffDate; d = d.AddDays(-1))
            {
                yield return new DayCompleteEvent(d.ToString("yyyy-MM-dd"), false, []);
                await Task.Delay(40, ct);
            }
        }
        else
        {
            // No posts found in window at all — mark every day as empty
            for (var d = today; d >= cutoffDate; d = d.AddDays(-1))
            {
                yield return new DayCompleteEvent(d.ToString("yyyy-MM-dd"), false, []);
                await Task.Delay(40, ct);
            }
        }

        // Signal that we're about to calculate — lets the client show a building state
        yield return new CalculatingEvent();

        // Dramatic pause before the tier reveal
        await Task.Delay(600, ct);

        // Final score
        var score = totalImagePosts == 0 ? 0.0 : compliantPosts * 100.0 / totalImagePosts;
        var tier = DetermineTier(score, totalImagePosts, config);
        yield return new DoneEvent(tier.ToString(), score, totalImagePosts, compliantPosts);
    }

    private static LabelTier DetermineTier(double score, int total, ScoringConfig config)
    {
        if (score >= config.HeroThreshold && total >= config.HeroMinimumImagePosts) return LabelTier.Hero;
        if (score >= config.GoldThreshold) return LabelTier.Gold;
        if (score >= config.SilverThreshold) return LabelTier.Silver;
        if (score >= config.BronzeThreshold) return LabelTier.Bronze;
        return LabelTier.None;
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

    /// <summary>
    /// Resolves a DID to its PDS (Personal Data Server) endpoint by fetching the DID document.
    /// did:plc → plc.directory; did:web → /.well-known/did.json on the domain.
    /// </summary>
    private static async Task<string> ResolvePdsAsync(string did, HttpClient http, CancellationToken ct)
    {
        var didDocUrl = did.StartsWith("did:web:")
            ? $"https://{did["did:web:".Length..]}/.well-known/did.json"
            : $"https://plc.directory/{Uri.EscapeDataString(did)}";

        var doc = await http.GetFromJsonAsync<JsonElement>(didDocUrl, JsonOpts, ct);

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
                            return ep.GetString()?.TrimEnd('/')
                                   ?? throw new InvalidOperationException("Empty PDS endpoint.");
                    }
                }
            }
        }

        throw new InvalidOperationException($"No #atproto_pds service found in DID document for {did}.");
    }

    private static List<PostInfo> BuildPostInfos(List<PostRecord> posts, int minLength) =>
        posts.Select(p =>
        {
            var imgs = p.Images
                .Select(img => new ImageInfo(img.AltText, img.AltText?.Length >= minLength))
                .ToList();
            return new PostInfo(p.AtUri, imgs.Count > 0 && imgs.All(i => i.IsCompliant), imgs);
        }).ToList();

    private record ListRecordsResponse(List<RecordItem>? Records, string? Cursor);
    private record RecordItem(string Uri, JsonElement Value);
}

public abstract record ScoringEvent;
public record ImageEvent(string Date) : ScoringEvent;
public record ImageInfo(string? AltText, bool IsCompliant);
public record PostInfo(string AtUri, bool IsCompliant, List<ImageInfo> Images);
public record DayCompleteEvent(string Date, bool AllCompliant, List<PostInfo> Posts) : ScoringEvent;
public record CalculatingEvent : ScoringEvent;
public record DoneEvent(string Tier, double Score, int TotalImagePosts, int CompliantPosts) : ScoringEvent;
