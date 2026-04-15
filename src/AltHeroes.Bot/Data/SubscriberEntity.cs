namespace AltHeroes.Bot.Data;

/// <summary>
/// Persisted record of a user who has liked the labeler's profile.
/// Active = true means they currently have a like; false means they unliked.
/// </summary>
public sealed class SubscriberEntity
{
    /// <summary>The subscriber's DID (primary key).</summary>
    public string Did { get; set; } = "";

    /// <summary>The rkey of their app.bsky.feed.like record, used to match delete events.</summary>
    public string RKey { get; set; } = "";

    /// <summary>True while the like exists; set to false on unlike.</summary>
    public bool Active { get; set; } = true;

    /// <summary>When the like was first observed via Jetstream.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Last time this record was updated (re-like or unlike).</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
