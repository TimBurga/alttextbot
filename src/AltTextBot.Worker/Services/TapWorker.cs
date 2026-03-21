using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using AltTextBot.Application.Configuration;
using AltTextBot.Application.Interfaces;
using AltTextBot.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AltTextBot.Worker.Services;

public class TapWorker(
    IServiceProvider serviceProvider,
    ISubscriberSet subscriberSet,
    IOptions<TapOptions> tapOptions,
    WorkerHealthMonitor healthMonitor,
    ILogger<TapWorker> logger) : BackgroundService
{
    private readonly TapOptions _tap = tapOptions.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("TapWorker: Waiting for subscriber set initialization...");
        await subscriberSet.WaitForInitializationAsync(stoppingToken);
        logger.LogInformation("TapWorker: Starting Tap channel connection.");

        var wsUrl = BuildChannelUrl();
        var delay = TimeSpan.FromSeconds(5);
        var maxDelay = TimeSpan.FromMinutes(2);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ConnectAndProcessAsync(wsUrl, stoppingToken);
                // Clean server-initiated close — reset backoff and wait base delay before reconnecting
                delay = TimeSpan.FromSeconds(5);
                logger.LogInformation("TapWorker: Connection closed cleanly. Reconnecting in {Delay}s...", delay.TotalSeconds);
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                healthMonitor.SetTapConnected(false);
                logger.LogError(ex, "TapWorker: Connection error. Reconnecting in {Delay}s...", delay.TotalSeconds);
                await Task.Delay(delay, stoppingToken);
                delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 2, maxDelay.TotalMilliseconds));
            }
        }

        logger.LogInformation("TapWorker: Stopped.");
    }

    private string BuildChannelUrl()
    {
        var baseUrl = _tap.BaseUrl.TrimEnd('/');
        return baseUrl.Replace("https://", "wss://").Replace("http://", "ws://") + "/channel";
    }

    private async Task ConnectAndProcessAsync(string wsUrl, CancellationToken ct)
    {
        logger.LogInformation("TapWorker: Connecting to {Url}", wsUrl);
        using var ws = new ClientWebSocket();
        await ws.ConnectAsync(new Uri(wsUrl), ct);
        healthMonitor.SetTapConnected(true);
        logger.LogInformation("TapWorker: Connected to Tap channel.");

        var buffer = new byte[64 * 1024];
        using var messageBuffer = new MemoryStream();

        while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
        {
            var result = await ws.ReceiveAsync(buffer, ct);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                healthMonitor.SetTapConnected(false);
                logger.LogWarning("TapWorker: Tap closed the connection.");
                break;
            }

            messageBuffer.Write(buffer, 0, result.Count);

            if (result.EndOfMessage)
            {
                var message = Encoding.UTF8.GetString(messageBuffer.ToArray());
                messageBuffer.SetLength(0);
                var eventId = await ProcessMessageAsync(message, ct);
                if (eventId.HasValue)
                    await SendAckAsync(ws, eventId.Value, ct);
            }
        }
    }

    private static async Task SendAckAsync(ClientWebSocket ws, uint id, CancellationToken ct)
    {
        var ack = Encoding.UTF8.GetBytes($"{{\"type\":\"ack\",\"id\":{id}}}");
        await ws.SendAsync(ack, WebSocketMessageType.Text, endOfMessage: true, ct);
    }

    // Returns the event ID to ack, or null if no ack is needed (unrecognised message type).
    private async Task<uint?> ProcessMessageAsync(string message, CancellationToken ct)
    {
        try
        {
            using var doc = JsonDocument.Parse(message);
            var root = doc.RootElement;

            var eventId = root.TryGetProperty("id", out var idEl) ? (uint?)idEl.GetUInt32() : null;

            if (!root.TryGetProperty("type", out var typeEl) || typeEl.GetString() != "record")
                return eventId;

            if (!root.TryGetProperty("record", out var eventRecord))
                return eventId;

            var collection = eventRecord.TryGetProperty("collection", out var colEl) ? colEl.GetString() ?? "" : "";
            var action = eventRecord.TryGetProperty("action", out var actEl) ? actEl.GetString() ?? "" : "";

            if (collection != "app.bsky.feed.post" || action != "create")
                return eventId;

            var did = eventRecord.TryGetProperty("did", out var didEl) ? didEl.GetString() ?? "" : "";
            var rkey = eventRecord.TryGetProperty("rkey", out var rkeyEl) ? rkeyEl.GetString() ?? "" : "";

            if (!eventRecord.TryGetProperty("record", out var record))
                return eventId;

            var atUri = $"at://{did}/{collection}/{rkey}";
            await HandlePostAsync(atUri, did, record, ct);
            return eventId;
        }
        catch (JsonException ex)
        {
            logger.LogDebug(ex, "TapWorker: Failed to parse message.");
            return null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "TapWorker: Error processing message.");
            return null;
        }
    }

    private async Task HandlePostAsync(string atUri, string did, JsonElement record, CancellationToken ct)
    {
        var (hasImages, imageCount, altTextCount, imageData) = ParseImageEmbed(record);
        if (!hasImages) return;

        DateTimeOffset? postedAt = null;
        if (record.TryGetProperty("createdAt", out var createdAtEl)
            && DateTimeOffset.TryParse(createdAtEl.GetString(), out var parsed))
        {
            postedAt = parsed;
        }

        using var scope = serviceProvider.CreateScope();
        var postTracking = scope.ServiceProvider.GetRequiredService<IPostTrackingService>();
        var auditLogger = scope.ServiceProvider.GetRequiredService<IAuditLogger>();

        await postTracking.RecordPostAsync(atUri, did, postedAt, hasImages, imageCount, altTextCount, imageData, ct);

        var eventType = altTextCount < imageCount
            ? AuditEventType.ImagePostMissingAlt
            : AuditEventType.ImagePostReceived;
        await auditLogger.LogAsync(eventType, did, $"Post {atUri}: {altTextCount}/{imageCount} images have alt text.", ct);
    }

    private static (bool hasImages, int imageCount, int altTextCount, IReadOnlyList<TrackedImageData> images) ParseImageEmbed(JsonElement record)
    {
        if (!record.TryGetProperty("embed", out var embed))
            return (false, 0, 0, []);

        var embedType = embed.TryGetProperty("$type", out var typeEl) ? typeEl.GetString() ?? "" : "";

        if (embedType == "app.bsky.embed.images")
            return CountImages(embed);

        if (embedType == "app.bsky.embed.recordWithMedia"
            && embed.TryGetProperty("media", out var media)
            && media.TryGetProperty("$type", out var mtEl)
            && mtEl.GetString() == "app.bsky.embed.images")
        {
            return CountImages(media);
        }

        return (false, 0, 0, []);
    }

    private static (bool hasImages, int imageCount, int altTextCount, IReadOnlyList<TrackedImageData> images) CountImages(JsonElement embedEl)
    {
        if (!embedEl.TryGetProperty("images", out var images) || images.ValueKind != JsonValueKind.Array)
            return (false, 0, 0, []);

        var result = new List<TrackedImageData>();
        var index = 0;
        foreach (var img in images.EnumerateArray())
        {
            var alt = img.TryGetProperty("alt", out var altEl) ? altEl.GetString() ?? "" : "";
            var hasAlt = !string.IsNullOrWhiteSpace(alt);

            // Blob CID: new-style ref.$link, fallback to old-style $link
            var cid = "";
            if (img.TryGetProperty("image", out var imageEl))
            {
                if (imageEl.TryGetProperty("ref", out var refEl) && refEl.TryGetProperty("$link", out var linkEl))
                    cid = linkEl.GetString() ?? "";
                else if (imageEl.TryGetProperty("$link", out var directLink))
                    cid = directLink.GetString() ?? "";
            }

            result.Add(new TrackedImageData(index, cid, hasAlt));
            index++;
        }

        var altCount = result.Count(i => i.HasAlt);
        return (true, result.Count, altCount, result);
    }
}
