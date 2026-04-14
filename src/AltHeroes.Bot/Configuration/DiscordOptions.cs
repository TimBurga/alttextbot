namespace AltHeroes.Bot.Configuration;

public sealed class DiscordOptions
{
    public const string SectionName = "Discord";

    /// <summary>Discord incoming webhook URL. Leave empty to disable shutdown notifications.</summary>
    public string? WebhookUrl { get; init; }
}
