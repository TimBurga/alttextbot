using System.ComponentModel.DataAnnotations;

namespace AltHeroes.Bot.Configuration;

public class AdminOptions
{
    public const string SectionName = "Admin";

    [Required]
    [StringLength(256, MinimumLength = 32)]
    public string ApiKey { get; init; } = "changeme-api-key";
}
