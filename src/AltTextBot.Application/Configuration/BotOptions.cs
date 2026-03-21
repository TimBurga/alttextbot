namespace AltTextBot.Application.Configuration;

public class BotOptions
{
    public const string SectionName = "Bot";

    public string Did { get; set; } = default!;
    public string Handle { get; set; } = default!;
    public string AppPassword { get; set; } = default!;
}
