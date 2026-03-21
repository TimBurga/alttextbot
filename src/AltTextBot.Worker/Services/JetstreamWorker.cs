using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using AltTextBot.Application.Configuration;
using AltTextBot.Application.Interfaces;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AltTextBot.Worker.Services;

public class JetstreamWorker(
    IServiceProvider serviceProvider,
    ISubscriberSet subscriberSet,
    IOptions<BotOptions> botOptions,
    IOptions<JetstreamOptions> jetstreamOptions,
    WorkerHealthMonitor healthMonitor,
    ILogger<JetstreamWorker> logger) : BackgroundService
{
    private readonly JetstreamOptions _jetstream = jetstreamOptions.Value;
    private readonly string _profileUri = $"at://{botOptions.Value.Did}/app.bsky.actor.profile/self";
    private int _eventsSinceFlush;
    private long _lastTimeUs;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("JetstreamWorker: Waiting for subscriber set initialization...");
        await subscriberSet.WaitForInitializationAsync(stoppingToken);
        logger.LogInformation("JetstreamWorker: Starting Jetstream connection.");

        var delay = TimeSpan.FromMilliseconds(_jetstream.ReconnectBaseDelayMs);
        var maxDelay = TimeSpan.FromMilliseconds(_jetstream.ReconnectMaxDelayMs);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ConnectAndProcessAsync(stoppingToken);
                // Clean server-initiated close — reset backoff and wait base delay before reconnecting
                delay = TimeSpan.FromMilliseconds(_jetstream.ReconnectBaseDelayMs);
                logger.LogInformation("JetstreamWorker: Connection closed cleanly. Reconnecting in {Delay}s...", delay.TotalSeconds);
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                healthMonitor.SetJetstreamConnected(false);
                logger.LogError(ex, "JetstreamWorker: Connection error. Reconnecting in {Delay}s...", delay.TotalSeconds);
                await Task.Delay(delay, stoppingToken);
                delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 2, maxDelay.TotalMilliseconds));
            }
        }

        logger.LogInformation("JetstreamWorker: Stopped.");
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_lastTimeUs > 0)
        {
            try
            {
                using var scope = serviceProvider.CreateScope();
                var repo = scope.ServiceProvider.GetRequiredService<IFirehoseStateRepository>();
                await repo.SaveCursorAsync(_lastTimeUs, cancellationToken);
                logger.LogInformation("JetstreamWorker: Flushed cursor {Cursor} on shutdown.", _lastTimeUs);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "JetstreamWorker: Failed to flush cursor on shutdown.");
            }
        }
        await base.StopAsync(cancellationToken);
    }

    private async Task ConnectAndProcessAsync(CancellationToken ct)
    {
        // Get current cursor
        long cursor;
        using (var scope = serviceProvider.CreateScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IFirehoseStateRepository>();
            cursor = await repo.GetCursorAsync(ct);
        }

        var url = BuildJetstreamUrl(cursor);
        logger.LogInformation("JetstreamWorker: Connecting to {Url}", url);

        using var ws = new ClientWebSocket();
        await ws.ConnectAsync(new Uri(url), ct);
        healthMonitor.SetJetstreamConnected(true);
        logger.LogInformation("JetstreamWorker: Connected to Jetstream.");

        var buffer = new byte[64 * 1024];
        using var messageBuffer = new MemoryStream();

        while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
        {
            var result = await ws.ReceiveAsync(buffer, ct);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                healthMonitor.SetJetstreamConnected(false);
                logger.LogWarning("JetstreamWorker: Server closed connection.");
                break;
            }

            messageBuffer.Write(buffer, 0, result.Count);

            if (result.EndOfMessage)
            {
                var message = Encoding.UTF8.GetString(messageBuffer.ToArray());
                messageBuffer.SetLength(0);
                await ProcessMessageAsync(message, ct);
            }
        }
    }

    private string BuildJetstreamUrl(long cursor)
    {
        var url = _jetstream.Url;
        var sep = url.Contains('?') ? "&" : "?";
        url += $"{sep}wantedCollections=app.bsky.feed.like";
        if (cursor > 0)
            url += $"&cursor={cursor}";
        return url;
    }

    private async Task ProcessMessageAsync(string message, CancellationToken ct)
    {
        try
        {
            using var doc = JsonDocument.Parse(message);
            var root = doc.RootElement;

            if (!root.TryGetProperty("kind", out var kindEl) || kindEl.GetString() != "commit")
                return;

            if (!root.TryGetProperty("commit", out var commit)) return;
            if (!root.TryGetProperty("did", out var didEl)) return;

            var did = didEl.GetString() ?? "";
            var collection = commit.TryGetProperty("collection", out var colEl) ? colEl.GetString() ?? "" : "";
            var operation = commit.TryGetProperty("operation", out var opEl) ? opEl.GetString() ?? "" : "";

            // Track cursor
            if (root.TryGetProperty("time_us", out var timeUsEl) && timeUsEl.TryGetInt64(out var timeUs))
            {
                _lastTimeUs = timeUs;
                _eventsSinceFlush++;
                if (_eventsSinceFlush >= _jetstream.CursorFlushIntervalEvents)
                {
                    _eventsSinceFlush = 0;
                    using var scope = serviceProvider.CreateScope();
                    var repo = scope.ServiceProvider.GetRequiredService<IFirehoseStateRepository>();
                    await repo.SaveCursorAsync(timeUs, ct);
                }
            }

            switch (collection)
            {
                case "app.bsky.feed.like" when operation == "create":
                    await HandleLikeCreateAsync(did, commit, ct);
                    break;

                case "app.bsky.feed.like" when operation == "delete":
                    await HandleLikeDeleteAsync(did, ct);
                    break;
            }
        }
        catch (JsonException ex)
        {
            logger.LogDebug(ex, "JetstreamWorker: Failed to parse message.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "JetstreamWorker: Error processing message.");
        }
    }

    private async Task HandleLikeCreateAsync(string did, JsonElement commit, CancellationToken ct)
    {
        if (!commit.TryGetProperty("record", out var record)) return;
        if (!record.TryGetProperty("subject", out var subject)) return;
        if (!subject.TryGetProperty("uri", out var uriEl)) return;

        var uri = uriEl.GetString() ?? "";
        if (uri != _profileUri) return;

        logger.LogInformation("JetstreamWorker: New like from {Did}", did);

        // Resolve handle (best-effort)
        var handle = await ResolveHandleAsync(did, ct) ?? did;

        using var scope = serviceProvider.CreateScope();
        var subscriberService = scope.ServiceProvider.GetRequiredService<ISubscriberService>();
        await subscriberService.SubscribeAsync(did, handle, ct);
    }

    private async Task HandleLikeDeleteAsync(string authorDid, CancellationToken ct)
    {
        // Jetstream delete events don't include the subject URI, so a like delete from a known subscriber
        // could be for any post — not necessarily ours. Verify via API before unsubscribing.
        if (!subscriberSet.Contains(authorDid)) return;

        using var scope = serviceProvider.CreateScope();
        var blueskyApi = scope.ServiceProvider.GetRequiredService<IBlueskyApiClient>();
        var stillLiking = await blueskyApi.HasLikedAsync(authorDid, _profileUri, ct);
        if (stillLiking) return;

        logger.LogInformation("JetstreamWorker: Unlike from {Did} confirmed — unsubscribing.", authorDid);
        var subscriberService = scope.ServiceProvider.GetRequiredService<ISubscriberService>();
        await subscriberService.UnsubscribeAsync(authorDid, ct);
    }

    private async Task<string?> ResolveHandleAsync(string did, CancellationToken ct)
    {
        try
        {
            using var scope = serviceProvider.CreateScope();
            var blueskyApi = scope.ServiceProvider.GetRequiredService<IBlueskyApiClient>();
            return await blueskyApi.GetHandleAsync(did, ct);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "JetstreamWorker: Could not resolve handle for {Did}, falling back to DID.", did);
            return null;
        }
    }
}
