namespace AltHeroes.Bot.Data;

public sealed class SubscriberEntity
{
    public string Did { get; set; } = "";
    public string RKey { get; set; } = "";
    public bool Active { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
