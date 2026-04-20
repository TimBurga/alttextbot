using System.ComponentModel.DataAnnotations;
using AltHeroes.Core;

namespace AltHeroes.Bot.Configuration;

public class ScoringOptions
{
    public const string SectionName = "Scoring";

    [Range(1, 365)]
    public int WindowDays { get; init; } = 30;

    [Range(1, 100)]
    public int AltTextMinimumLength { get; init; } = 5;

    [Range(0, int.MaxValue)]
    public int HeroMinimumImagePosts { get; init; } = 3;

    [Range(0.0, 100.0)]
    public double HeroThreshold { get; init; } = 100.0;

    [Range(0.0, 100.0)]
    public double GoldThreshold { get; init; } = 95.0;

    [Range(0.0, 100.0)]
    public double SilverThreshold { get; init; } = 85.0;

    [Range(0.0, 100.0)]
    public double BronzeThreshold { get; init; } = 70.0;

    public ScoringConfig ToConfig() => new()
    {
        WindowDays = WindowDays,
        AltTextMinimumLength = AltTextMinimumLength,
        HeroMinimumImagePosts = HeroMinimumImagePosts,
        HeroThreshold = HeroThreshold,
        GoldThreshold = GoldThreshold,
        SilverThreshold = SilverThreshold,
        BronzeThreshold = BronzeThreshold,
    };
}
