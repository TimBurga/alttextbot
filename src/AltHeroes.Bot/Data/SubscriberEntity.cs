namespace AltHeroes.Bot.Data;

public sealed class SubscriberEntity
{
    public string Did { get; set; } = string.Empty;
    public bool Blocked { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
