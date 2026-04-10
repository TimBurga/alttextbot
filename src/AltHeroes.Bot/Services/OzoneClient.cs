using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AltHeroes.Bot.Configuration;
using AltHeroes.Core;
using Microsoft.Extensions.Options;

namespace AltHeroes.Bot.Services;

/// <summary>
/// Authenticates with Ozone/PDS and applies or negates moderation labels via
/// tools.ozone.moderation.emitEvent. Also queries current labels via
/// com.atproto.label.queryLabels.
/// Session is created lazily and cached in-memory (respects 300/day limit).
/// Registered as a singleton; creates HttpClients via IHttpClientFactory.
/// </summary>
public sealed class OzoneClient(
    IHttpClientFactory httpClientFactory,
    IOptions<BotOptions> botOptions,
    IOptions<LabelerOptions> labelerOptions,
    ILogger<OzoneClient> logger)
{
    private readonly BotOptions _bot = botOptions.Value;
    private readonly LabelerOptions _labeler = labelerOptions.Value;
    private HttpClient Http => httpClientFactory.CreateClient(nameof(OzoneClient));
    private readonly SemaphoreSlim _sessionLock = new(1, 1);
    private string? _accessJwt;

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    // ── Label names ──────────────────────────────────────────────────────────

    private static string TierName(LabelTier tier) => tier switch
    {
        LabelTier.Bronze => "altheroes-bronze",
        LabelTier.Silver => "altheroes-silver",
        LabelTier.Gold => "altheroes-gold",
        LabelTier.Hero => "altheroes-hero",
        _ => throw new ArgumentOutOfRangeException(nameof(tier))
    };

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>Reads the current (highest) active label for a DID from the labeler.</summary>
    public async Task<LabelTier> QueryCurrentTierAsync(string did, CancellationToken ct = default)
    {
        var url = $"https://public.api.bsky.app/xrpc/com.atproto.label.queryLabels" +
                  $"?sources={Uri.EscapeDataString(_labeler.Did)}" +
                  $"&uriPatterns={Uri.EscapeDataString(did)}";

        QueryLabelsResponse? resp;
        try
        {
            resp = await Http.GetFromJsonAsync<QueryLabelsResponse>(url, JsonOpts, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "OzoneClient: Failed to query labels for {Did}.", did);
            return LabelTier.None;
        }

        if (resp?.Labels is not { Count: > 0 }) return LabelTier.None;

        // Active labels = created, not negated (neg == true marks a negation)
        var active = resp.Labels
            .Where(l => l.Neg != true)
            .Select(l => l.Val)
            .ToHashSet();

        // Return highest active tier
        if (active.Contains(TierName(LabelTier.Hero))) return LabelTier.Hero;
        if (active.Contains(TierName(LabelTier.Gold))) return LabelTier.Gold;
        if (active.Contains(TierName(LabelTier.Silver))) return LabelTier.Silver;
        if (active.Contains(TierName(LabelTier.Bronze))) return LabelTier.Bronze;
        return LabelTier.None;
    }

    /// <summary>Applies a new label and negates the old one atomically via emitEvent.</summary>
    public async Task UpdateLabelAsync(
        string did,
        LabelTier newTier,
        LabelTier oldTier,
        CancellationToken ct = default)
    {
        var createVals = newTier != LabelTier.None ? new[] { TierName(newTier) } : Array.Empty<string>();
        var negateVals = oldTier != LabelTier.None ? new[] { TierName(oldTier) } : Array.Empty<string>();

        if (createVals.Length == 0 && negateVals.Length == 0) return;

        await EmitLabelEventAsync(did, createVals, negateVals, ct);
    }

    /// <summary>Negates all known tier labels for a DID (used on unenroll).</summary>
    public async Task RemoveAllLabelsAsync(string did, LabelTier currentTier, CancellationToken ct = default)
    {
        if (currentTier == LabelTier.None) return;
        await EmitLabelEventAsync(did, [], [TierName(currentTier)], ct);
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private async Task EmitLabelEventAsync(
        string did,
        string[] createVals,
        string[] negateVals,
        CancellationToken ct)
    {
        var jwt = await GetAccessJwtAsync(ct);
        if (jwt is null) { logger.LogWarning("OzoneClient: No session — skipping label update for {Did}.", did); return; }

        var body = new EmitEventRequest(
            Event: new ModEventLabel(createVals, negateVals),
            Subject: new RepoRef(did),
            CreatedBy: _bot.Did
        );

        var url = $"{_labeler.OzoneUrl.TrimEnd('/')}/xrpc/tools.ozone.moderation.emitEvent";

        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(body, options: JsonOpts)
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        HttpResponseMessage resp;
        try
        {
            resp = await Http.SendAsync(req, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "OzoneClient: Failed to emit label event for {Did}.", did);
            return;
        }

        if (!resp.IsSuccessStatusCode)
        {
            // 401 → token expired; clear so next call re-authenticates
            if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                logger.LogWarning("OzoneClient: 401 on emitEvent — clearing session.");
                _accessJwt = null;
            }
            var body2 = await resp.Content.ReadAsStringAsync(ct);
            logger.LogWarning("OzoneClient: emitEvent HTTP {Status} for {Did}: {Body}", resp.StatusCode, did, body2);
        }
        else
        {
            logger.LogInformation("OzoneClient: Label updated for {Did}: +[{Create}] -[{Negate}]",
                did, string.Join(", ", createVals), string.Join(", ", negateVals));
        }
    }

    private async Task<string?> GetAccessJwtAsync(CancellationToken ct)
    {
        if (_accessJwt is not null) return _accessJwt;

        await _sessionLock.WaitAsync(ct);
        try
        {
            if (_accessJwt is not null) return _accessJwt;

            if (string.IsNullOrEmpty(_bot.AppPassword))
            {
                logger.LogWarning("OzoneClient: Bot:AppPassword not configured — label writes disabled.");
                return null;
            }

            var pdsUrl = await ResolvePdsUrlAsync(ct) ?? "https://bsky.social";
            var resp = await Http.PostAsJsonAsync(
                $"{pdsUrl}/xrpc/com.atproto.server.createSession",
                new { identifier = _bot.Did, password = _bot.AppPassword },
                cancellationToken: ct);

            resp.EnsureSuccessStatusCode();

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            _accessJwt = doc.RootElement.GetProperty("accessJwt").GetString();
            logger.LogInformation("OzoneClient: Session created for {Did}.", _bot.Did);
            return _accessJwt;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "OzoneClient: Failed to create session.");
            return null;
        }
        finally { _sessionLock.Release(); }
    }

    private async Task<string?> ResolvePdsUrlAsync(CancellationToken ct)
    {
        // Resolve the PDS for the bot DID via the directory
        try
        {
            var resp = await Http.GetFromJsonAsync<JsonElement>(
                $"https://plc.directory/{Uri.EscapeDataString(_bot.Did)}",
                cancellationToken: ct);

            if (resp.TryGetProperty("service", out var services))
            {
                foreach (var svc in services.EnumerateArray())
                {
                    if (svc.TryGetProperty("type", out var t) && t.GetString() == "AtprotoPersonalDataServer" &&
                        svc.TryGetProperty("serviceEndpoint", out var ep))
                        return ep.GetString();
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "OzoneClient: Could not resolve PDS for {Did}, using bsky.social.", _bot.Did);
        }
        return null;
    }

    // ── Request / response shapes ─────────────────────────────────────────────

    private record EmitEventRequest(
        [property: JsonPropertyName("event")] ModEventLabel Event,
        [property: JsonPropertyName("subject")] RepoRef Subject,
        [property: JsonPropertyName("createdBy")] string CreatedBy
    );

    private record ModEventLabel(
        [property: JsonPropertyName("createLabelVals")] string[] CreateLabelVals,
        [property: JsonPropertyName("negateLabelVals")] string[] NegateLabelVals
    )
    {
        [JsonPropertyName("$type")]
        public string Type => "tools.ozone.moderation.defs#modEventLabel";
    }

    private record RepoRef(
        [property: JsonPropertyName("did")] string Did
    )
    {
        [JsonPropertyName("$type")]
        public string Type => "com.atproto.admin.defs#repoRef";
    }

    private record QueryLabelsResponse(List<LabelItem>? Labels);
    private record LabelItem(string Val, bool? Neg);
}
