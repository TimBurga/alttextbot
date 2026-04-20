using System.ComponentModel.DataAnnotations;

namespace AltHeroes.Bot.Configuration;

public class LabelerOptions
{
    public const string SectionName = "Labeler";

    [Required]
    [StringLength(256)]
    public required string Did { get; init; }

    [Required]
    [Url]
    public required string OzoneUrl { get; init; }
}
