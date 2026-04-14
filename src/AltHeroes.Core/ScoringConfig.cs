namespace AltHeroes.Core;

public record ScoringConfig
{
    public int WindowDays { get; init; } = 30;
    public int AltTextMinimumLength { get; init; } = 5;
    public int HeroMinimumImagePosts { get; init; } = 3;
    public double HeroThreshold { get; init; } = 100.0;
    public double GoldThreshold { get; init; } = 95.0;
    public double SilverThreshold { get; init; } = 85.0;
    public double BronzeThreshold { get; init; } = 70.0;
}
