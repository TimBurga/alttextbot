namespace AltTextBot.Application.Interfaces;

public interface IBlueskyApiClient
{
    Task<string?> GetHandleAsync(string did, CancellationToken ct = default);
    Task<LikesPage> GetLikesPageAsync(string uri, string? cursor, CancellationToken ct = default);
    Task<bool> HasLikedAsync(string actorDid, string subjectUri, CancellationToken ct = default);
}

public record LikesPage(IReadOnlyList<LikeActor> Likes, string? Cursor);
public record LikeActor(string Did, string Handle);
