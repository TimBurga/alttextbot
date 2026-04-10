namespace AltHeroes.Web.Configuration;

public class BotClientOptions
{
    public const string SectionName = "BotClient";
    /// <summary>Base URL of the AltHeroes.Bot service (via service discovery).</summary>
    public string BaseUrl { get; set; } = "http://bot";
    /// <summary>The X-Admin-Key value accepted by the Bot's admin endpoints.</summary>
    public string AdminApiKey { get; set; } = "changeme-api-key";
}
