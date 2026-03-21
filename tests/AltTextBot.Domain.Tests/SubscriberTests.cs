using AltTextBot.Domain.Entities;
using AltTextBot.Domain.Enums;
using FluentAssertions;

namespace AltTextBot.Domain.Tests;

public class SubscriberTests
{
    [Fact]
    public void Create_SetsActiveStatus()
    {
        var subscriber = Subscriber.Create("did:plc:abc", "alice.bsky.social");

        subscriber.Status.Should().Be(SubscriberStatus.Active);
        subscriber.Did.Should().Be("did:plc:abc");
        subscriber.Handle.Should().Be("alice.bsky.social");
        subscriber.SubscribedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Deactivate_ChangesStatusToDeactivated()
    {
        var subscriber = Subscriber.Create("did:plc:abc", "alice.bsky.social");

        subscriber.Deactivate();

        subscriber.Status.Should().Be(SubscriberStatus.Deactivated);
    }

    [Fact]
    public void Reactivate_ChangesStatusToActive()
    {
        var subscriber = Subscriber.Create("did:plc:abc", "alice.bsky.social");
        subscriber.Deactivate();

        subscriber.Reactivate();

        subscriber.Status.Should().Be(SubscriberStatus.Active);
    }

    [Fact]
    public void RecordScored_SetsLastScoredAt()
    {
        var subscriber = Subscriber.Create("did:plc:abc", "alice.bsky.social");

        subscriber.RecordScored();

        subscriber.LastScoredAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void UpdateHandle_ChangesHandle()
    {
        var subscriber = Subscriber.Create("did:plc:abc", "alice.bsky.social");

        subscriber.UpdateHandle("alice-new.bsky.social");

        subscriber.Handle.Should().Be("alice-new.bsky.social");
    }
}
