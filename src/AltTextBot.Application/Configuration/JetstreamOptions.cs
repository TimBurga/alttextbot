namespace AltTextBot.Application.Configuration;

public class JetstreamOptions
{
    public const string SectionName = "Jetstream";

    public string Url { get; set; } = "wss://jetstream2.us-east.bsky.network/subscribe";
    public int CursorFlushIntervalEvents { get; set; } = 1000;
    public int ReconnectBaseDelayMs { get; set; } = 1000;
    public int ReconnectMaxDelayMs { get; set; } = 60000;
}
