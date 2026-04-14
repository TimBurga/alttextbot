using AltHeroes.Core;

namespace AltHeroes.Web.Configuration;

public sealed class ScoringOptions
{
    public const string SectionName = "Scoring";
    public int WindowDays { get; set; } = 30;
    public int AltTextMinimumLength { get; set; } = 5;
    public int HeroMinimumImagePosts { get; set; } = 3;
    public double HeroThreshold { get; set; } = 100.0;
    public double GoldThreshold { get; set; } = 95.0;
    public double SilverThreshold { get; set; } = 85.0;
    public double BronzeThreshold { get; set; } = 70.0;

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
