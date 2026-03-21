namespace AltTextBot.Web.Configuration;

public class AdminOptions
{
    public const string SectionName = "Admin";
    public string Password { get; set; } = "changeme";
    public string ApiKey { get; set; } = "changeme-api-key";
}
