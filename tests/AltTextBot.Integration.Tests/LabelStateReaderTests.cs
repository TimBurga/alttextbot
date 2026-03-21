using AltTextBot.Domain.Enums;
using AltTextBot.Infrastructure.Data;
using AltTextBot.Infrastructure.Services;

namespace AltTextBot.Integration.Tests;

public class LabelStateReaderTests(LabelerDatabaseFixture fixture) : IClassFixture<LabelerDatabaseFixture>
{
    private async Task SeedLabel(string did, string val, bool neg = false)
    {
        // NoTracking only affects query results; Add/SaveChanges still works fine
        await using var db = fixture.CreateDbContext();
        db.Labels.Add(new LabelerLabel { Did = did, Val = val, Neg = neg });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task GetCurrentLabelAsync_WhenNoLabels_ReturnsNull()
    {
        await using var db = fixture.CreateDbContext();
        var reader = new LabelStateReader(db);

        var result = await reader.GetCurrentLabelAsync("did:plc:label-none-1");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetCurrentLabelAsync_WithActiveLabel_ReturnsCorrectTier()
    {
        var did = "did:plc:label-active-1";
        await SeedLabel(did, "Gold");

        await using var db = fixture.CreateDbContext();
        var reader = new LabelStateReader(db);

        var result = await reader.GetCurrentLabelAsync(did);

        result.Should().Be(LabelTier.Gold);
    }

    [Fact]
    public async Task GetCurrentLabelAsync_WithNegatedLabel_ReturnsNull()
    {
        var did = "did:plc:label-neg-1";
        await SeedLabel(did, "Bronze", neg: true);

        await using var db = fixture.CreateDbContext();
        var reader = new LabelStateReader(db);

        var result = await reader.GetCurrentLabelAsync(did);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetCurrentLabelAsync_WithMultipleTiers_ReturnsHighest()
    {
        var did = "did:plc:label-highest-1";
        await SeedLabel(did, "Bronze");
        await SeedLabel(did, "Silver");

        await using var db = fixture.CreateDbContext();
        var reader = new LabelStateReader(db);

        var result = await reader.GetCurrentLabelAsync(did);

        result.Should().Be(LabelTier.Silver);
    }

    [Fact]
    public async Task GetCurrentLabelAsync_WithUnknownLabelVal_ReturnsNull()
    {
        var did = "did:plc:label-unknown-1";
        await SeedLabel(did, "some-other-label");

        await using var db = fixture.CreateDbContext();
        var reader = new LabelStateReader(db);

        var result = await reader.GetCurrentLabelAsync(did);

        result.Should().BeNull();
    }
}
