namespace AltTextBot.Application.Configuration;

public class ScoringOptions
{
    public const string SectionName = "Scoring";

    public double HeroThreshold { get; set; } = 95.0;
    public double GoldThreshold { get; set; } = 80.0;
    public double SilverThreshold { get; set; } = 60.0;
    public double BronzeThreshold { get; set; } = 40.0;
    public int RollingWindowDays { get; set; } = 30;
    public int RescoringIntervalMinutes { get; set; } = 60;
}
