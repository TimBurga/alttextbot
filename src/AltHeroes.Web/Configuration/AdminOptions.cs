namespace AltHeroes.Web.Configuration;

public class AdminOptions
{
    public const string SectionName = "Admin";
    /// <summary>Password for the web admin login form.</summary>
    public string Password { get; set; } = "changeme";
}
