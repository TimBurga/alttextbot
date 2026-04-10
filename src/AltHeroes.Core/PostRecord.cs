namespace AltHeroes.Core;

/// <summary>An image within a post, with its alt text (null if absent).</summary>
public record ImageRecord(string? AltText);

/// <summary>
/// A post fetched from listRecords. Text-only posts (no images) are
/// represented with an empty Images list and are invisible to scoring.
/// </summary>
public record PostRecord(string AtUri, DateTimeOffset CreatedAt, IReadOnlyList<ImageRecord> Images);
