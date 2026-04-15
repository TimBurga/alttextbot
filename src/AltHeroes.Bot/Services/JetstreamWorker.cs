using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using AltHeroes.Bot;
using AltHeroes.Bot.Configuration;
using AltHeroes.Bot.Data;
using AltHeroes.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AltHeroes.Bot.Services;

/// <summary>
/// Subscribes to Jetstream (app.bsky.feed.post + app.bsky.feed.like collections).
/// DID filtering is done client-side so the subscriber set can change dynamically
/// without reconnecting.
///
/// Waits for BotStartupService to complete before processing any events.
/// Like create/delete events are persisted to the database so subscribers survive restarts.
/// </summary>
public sealed class JetstreamWorker : BackgroundService
{
    private readonly BotState _state;
    private readonly IDbContextFactory<BotDbContext> _dbContextFactory;
    private readonly ListRecordsClient _listRecords;
    private readonly OzoneClient _ozone;
    private readonly LabelDiffService _diff;
    private readonly StartupGate _startupGate;
    private readonly JetstreamOptions _jetstream;
    private readonly ScoringConfig _scoringConfig;
    private readonly string _profileUri;
    private readonly ILogger<JetstreamWorker> _logger;
    private long _cursorUs;

    public JetstreamWorker(
        BotState state,
        IDbContextFactory<BotDbContext> dbContextFactory,
        ListRecordsClient listRecords,
        OzoneClient ozone,
        LabelDiffService diff,
        StartupGate startupGate,
        IOptions<BotOptions> botOptions,
        IOptions<LabelerOptions> labelerOptions,
        IOptions<JetstreamOptions> jetstreamOptions,
        IOptions<ScoringOptions> scoringOptions,
        ILogger<JetstreamWorker> logger)
    {
        _state = state;
        _dbContextFactory = dbContextFactory;
        _listRecords = listRecords;
        _ozone = ozone;
        _diff = diff;
        _startupGate = startupGate;
        _jetstream = jetstreamOptions.Value;
        _scoringConfig = scoringOptions.Value.ToConfig();
        _profileUri = $"at://{labelerOptions.Value.Did}/app.bsky.actor.profile/self";
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Capture T0 before waiting for startup so we replay any events that arrived
        // during the backfill window once Jetstream connects.
        _cursorUs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000;

        _logger.LogInformation("JetstreamWorker: Waiting for startup backfill to complete...");
        await _startupGate.WaitAsync(stoppingToken);
        _logger.LogInformation("JetstreamWorker: Startup complete — connecting to Jetstream from T0.");

        var delay = TimeSpan.FromMilliseconds(_jetstream.ReconnectBaseDelayMs);
        var maxDelay = TimeSpan.FromMilliseconds(_jetstream.ReconnectMaxDelayMs);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ConnectAndProcessAsync(stoppingToken);
                delay = TimeSpan.FromMilliseconds(_jetstream.ReconnectBaseDelayMs);
                _logger.LogInformation("JetstreamWorker: Connection closed cleanly. Reconnecting in {Delay}s...", delay.TotalSeconds);
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "JetstreamWorker: Connection error. Reconnecting in {Delay}s...", delay.TotalSeconds);
                await Task.Delay(delay, stoppingToken);
                delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 2, maxDelay.TotalMilliseconds));
            }
        }

        _logger.LogInformation("JetstreamWorker: Stopped.");
    }

    private async Task ConnectAndProcessAsync(CancellationToken ct)
    {
        var url = $"{_jetstream.Url}?wantedCollections=app.bsky.feed.post&wantedCollections=app.bsky.feed.like" +
                  (_cursorUs > 0 ? $"&cursor={_cursorUs}" : "");

        _logger.LogInformation("JetstreamWorker: Connecting to {Url}", url);

        using var ws = new ClientWebSocket();
        await ws.ConnectAsync(new Uri(url), ct);
        _logger.LogInformation("JetstreamWorker: Connected.");

        var buffer = new byte[64 * 1024];
        using var msgBuf = new MemoryStream();

        while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
        {
            var result = await ws.ReceiveAsync(buffer, ct);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                _logger.LogWarning("JetstreamWorker: Server closed connection.");
                break;
            }

            msgBuf.Write(buffer, 0, result.Count);
            if (!result.EndOfMessage) continue;

            var json = Encoding.UTF8.GetString(msgBuf.ToArray());
            msgBuf.SetLength(0);
            await ProcessMessageAsync(json, ct);
        }
    }

    private async Task ProcessMessageAsync(string json, CancellationToken ct)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("kind", out var kindEl) || kindEl.GetString() != "commit") return;
            if (!root.TryGetProperty("commit", out var commit)) return;
            if (!root.TryGetProperty("did", out var didEl)) return;

            var did = didEl.GetString() ?? "";
            var collection = commit.TryGetProperty("collection", out var colEl) ? colEl.GetString() ?? "" : "";
            var operation = commit.TryGetProperty("operation", out var opEl) ? opEl.GetString() ?? "" : "";

            if (root.TryGetProperty("time_us", out var timeUsEl) && timeUsEl.TryGetInt64(out var timeUs))
                _cursorUs = timeUs;

            switch (collection, operation)
            {
                case ("app.bsky.feed.post", "create"):
                    await HandlePostCreateAsync(did, ct);
                    break;

                case ("app.bsky.feed.like", "create"):
                    await HandleLikeCreateAsync(did, commit, ct);
                    break;

                case ("app.bsky.feed.like", "delete"):
                    await HandleLikeDeleteAsync(commit, ct);
                    break;
            }
        }
        catch (JsonException ex)
        {
            _logger.LogDebug(ex, "JetstreamWorker: Failed to parse message.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "JetstreamWorker: Error processing message.");
        }
    }

    // ── Handlers ─────────────────────────────────────────────────────────────

    private async Task HandlePostCreateAsync(string did, CancellationToken ct)
    {
        if (!_state.Contains(did)) return;

        _logger.LogDebug("JetstreamWorker: New post from subscriber {Did} — lazy rescoring.", did);
        await LazyRescoreAsync(did, ct);
    }

    private async Task HandleLikeCreateAsync(string did, JsonElement commit, CancellationToken ct)
    {
        if (!commit.TryGetProperty("record", out var record)) return;
        if (!record.TryGetProperty("subject", out var subject)) return;
        if (!subject.TryGetProperty("uri", out var uriEl)) return;
        if (uriEl.GetString() != _profileUri) return;

        var rkey = commit.TryGetProperty("rkey", out var rkeyEl) ? rkeyEl.GetString() ?? "" : "";
        if (string.IsNullOrEmpty(rkey)) return;

        _logger.LogInformation("JetstreamWorker: New subscriber like from {Did} (rkey={Rkey}).", did, rkey);

        // Persist the like to the database (upsert: insert on first like, update on re-like).
        var now = DateTimeOffset.UtcNow;
        await using (var db = _dbContextFactory.CreateDbContext())
        {
            var existing = await db.Subscribers.FindAsync([did], ct);
            if (existing is null)
            {
                db.Subscribers.Add(new SubscriberEntity
                {
                    Did = did,
                    RKey = rkey,
                    Active = true,
                    CreatedAt = now,
                    UpdatedAt = now,
                });
            }
            else
            {
                existing.RKey = rkey;
                existing.Active = true;
                existing.UpdatedAt = now;
            }
            await db.SaveChangesAsync(ct);
        }

        await EnrollLiveAsync(did, rkey, ct);
    }

    private async Task HandleLikeDeleteAsync(JsonElement commit, CancellationToken ct)
    {
        var rkey = commit.TryGetProperty("rkey", out var rkeyEl) ? rkeyEl.GetString() : null;
        if (rkey is null) return;

        var unenrolledDid = _state.Unenroll(rkey);
        if (unenrolledDid is null) return; // not one of our profile likes

        _logger.LogInformation("JetstreamWorker: Unlike detected — unenrolling {Did}.", unenrolledDid);

        // Deactivate in database (keep the row so we have audit history).
        await using (var db = _dbContextFactory.CreateDbContext())
        {
            var entity = await db.Subscribers.FindAsync([unenrolledDid], ct);
            if (entity is not null)
            {
                entity.Active = false;
                entity.UpdatedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(ct);
            }
        }

        var currentTier = _state.GetCurrentTier(unenrolledDid);
        await _ozone.RemoveAllLabelsAsync(unenrolledDid, currentTier, ct);
    }

    // ── Lazy rescore ──────────────────────────────────────────────────────────

    private async Task LazyRescoreAsync(string did, CancellationToken ct)
    {
        try
        {
            var posts = await _listRecords.GetPostsAsync(did, _scoringConfig.WindowDays, ct);
            var result = ScoringService.ComputeTier(posts, _scoringConfig, DateTimeOffset.UtcNow);
            await _diff.ApplyIfChangedAsync(did, did, result.Tier, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "JetstreamWorker: Lazy rescore failed for {Did}.", did);
        }
    }

    // ── Live enroll ───────────────────────────────────────────────────────────

    private async Task EnrollLiveAsync(string did, string rkey, CancellationToken ct)
    {
        if (_state.Contains(did))
        {
            _logger.LogDebug("JetstreamWorker: {Did} already enrolled.", did);
            return;
        }

        _state.Enroll(did, rkey);

        try
        {
            var currentTier = await _ozone.QueryCurrentTierAsync(did, ct);
            _state.SetCurrentTier(did, currentTier);

            var posts = await _listRecords.GetPostsAsync(did, _scoringConfig.WindowDays, ct);
            var result = ScoringService.ComputeTier(posts, _scoringConfig, DateTimeOffset.UtcNow);
            await _diff.ApplyIfChangedAsync(did, did, result.Tier, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "JetstreamWorker: Failed to score newly enrolled {Did}.", did);
        }
    }
}
