namespace AltTextBot.Application.Configuration;

public class LabelerOptions
{
    public const string SectionName = "Labeler";

    public string BaseUrl { get; set; } = default!;
    public string ApiKey { get; set; } = default!;
}
