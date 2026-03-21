using System.Net.Http.Json;
using AltTextBot.Application.Interfaces;

namespace AltTextBot.Infrastructure.Services;

public class BlueskyApiClient(HttpClient httpClient) : IBlueskyApiClient
{
    public async Task<string?> GetHandleAsync(string did, CancellationToken ct = default)
    {
        var response = await httpClient.GetFromJsonAsync<ProfileResponse>(
            $"/xrpc/app.bsky.actor.getProfile?actor={Uri.EscapeDataString(did)}", ct);
        return response?.Handle;
    }

    public async Task<LikesPage> GetLikesPageAsync(string uri, string? cursor, CancellationToken ct = default)
    {
        var url = $"/xrpc/app.bsky.feed.getLikes?uri={Uri.EscapeDataString(uri)}&limit=100";
        if (cursor is not null)
            url += $"&cursor={Uri.EscapeDataString(cursor)}";

        var response = await httpClient.GetFromJsonAsync<LikesResponse>(url, ct);
        return response is null
            ? new LikesPage([], null)
            : new LikesPage(response.Likes.Select(l => new LikeActor(l.Actor.Did, l.Actor.Handle)).ToList(), response.Cursor);
    }

    public async Task<bool> HasLikedAsync(string actorDid, string subjectUri, CancellationToken ct = default)
    {
        string? cursor = null;
        var pagesChecked = 0;
        const int maxPages = 20;

        do
        {
            var url = $"/xrpc/com.atproto.repo.listRecords?repo={Uri.EscapeDataString(actorDid)}&collection=app.bsky.feed.like&limit=100";
            if (cursor is not null)
                url += $"&cursor={Uri.EscapeDataString(cursor)}";

            var response = await httpClient.GetFromJsonAsync<ListRecordsResponse>(url, ct);
            if (response is null) return false;

            if (response.Records.Any(r => r.Value?.Subject?.Uri == subjectUri))
                return true;

            cursor = response.Cursor;
            pagesChecked++;

            // Treat as still liking if we exceed the limit — avoids false unsubscribes for
            // users with very large like histories.
            if (pagesChecked >= maxPages && cursor is not null)
                return true;
        }
        while (cursor is not null);

        return false;
    }

    private record ProfileResponse(string Handle);
    private record LikesResponse(string? Cursor, List<LikeRecord> Likes);
    private record LikeRecord(ActorRecord Actor);
    private record ActorRecord(string Did, string Handle);
    private record ListRecordsResponse(string? Cursor, List<LikeRecordEntry> Records);
    private record LikeRecordEntry(LikeRecordValue? Value);
    private record LikeRecordValue(LikeSubject? Subject);
    private record LikeSubject(string Uri);
}
