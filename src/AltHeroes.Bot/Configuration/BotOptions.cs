using System.ComponentModel.DataAnnotations;

namespace AltHeroes.Bot.Configuration;

public class BotOptions
{
    public const string SectionName = "Bot";

    [Required]
    public required string Did { get; init; }

    [Required]
    public required string Handle { get; init; }

    [Required]
    public required string AppPassword { get; init; }
}
