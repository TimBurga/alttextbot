using AltTextBot.Infrastructure.Services;
using FluentAssertions;

namespace AltTextBot.Application.Tests;

public class SubscriberSetTests
{
    [Fact]
    public void Contains_ReturnsFalse_WhenEmpty()
    {
        var set = new SubscriberSet();
        set.Contains("did:plc:abc").Should().BeFalse();
    }

    [Fact]
    public void Add_ThenContains_ReturnsTrue()
    {
        var set = new SubscriberSet();
        set.Add("did:plc:abc");
        set.Contains("did:plc:abc").Should().BeTrue();
    }

    [Fact]
    public void Remove_ThenContains_ReturnsFalse()
    {
        var set = new SubscriberSet();
        set.Add("did:plc:abc");
        set.Remove("did:plc:abc");
        set.Contains("did:plc:abc").Should().BeFalse();
    }

    [Fact]
    public void Initialize_AddsAllDids()
    {
        var set = new SubscriberSet();
        set.Initialize(["did:plc:a", "did:plc:b", "did:plc:c"]);

        set.Contains("did:plc:a").Should().BeTrue();
        set.Contains("did:plc:b").Should().BeTrue();
        set.Contains("did:plc:c").Should().BeTrue();
    }

    [Fact]
    public async Task WaitForInitializationAsync_CompletesAfterMarkInitialized()
    {
        var set = new SubscriberSet();

        var waitTask = set.WaitForInitializationAsync();
        waitTask.IsCompleted.Should().BeFalse();

        set.MarkInitialized();

        await waitTask.WaitAsync(TimeSpan.FromSeconds(1));
        waitTask.IsCompleted.Should().BeTrue();
    }
}
