using System.ComponentModel.DataAnnotations;

namespace AltHeroes.Bot.Configuration;

public class JetstreamOptions
{
    public const string SectionName = "Jetstream";

    [Required]
    public string Url { get; init; } = "wss://jetstream2.us-east.bsky.network/subscribe";

    [Range(100, 60000)]
    public int ReconnectBaseDelayMs { get; init; } = 1000;

    [Range(1000, 600000)]
    public int ReconnectMaxDelayMs { get; init; } = 60000;
}
