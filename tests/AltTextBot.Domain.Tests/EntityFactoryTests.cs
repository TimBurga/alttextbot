using AltTextBot.Domain.Entities;
using AltTextBot.Domain.Enums;
using FluentAssertions;

namespace AltTextBot.Domain.Tests;

public class EntityFactoryTests
{
    [Fact]
    public void FirehoseState_Create_SetsLastTimeUsToZero()
    {
        var state = FirehoseState.Create();

        state.LastTimeUs.Should().Be(0);
    }

    [Fact]
    public void FirehoseState_UpdateCursor_UpdatesLastTimeUs()
    {
        var state = FirehoseState.Create();

        state.UpdateCursor(1_234_567_890L);

        state.LastTimeUs.Should().Be(1_234_567_890L);
    }

    [Fact]
    public void FirehoseState_UpdateCursor_CanBeCalledMultipleTimes()
    {
        var state = FirehoseState.Create();
        state.UpdateCursor(100L);
        state.UpdateCursor(200L);

        state.LastTimeUs.Should().Be(200L);
    }

    [Fact]
    public void AuditLog_Create_SetsAllProperties()
    {
        var before = DateTimeOffset.UtcNow;

        var log = AuditLog.Create(AuditEventType.SubscriberAdded, "did:plc:abc", "New subscriber via like.");

        log.EventType.Should().Be(AuditEventType.SubscriberAdded);
        log.SubscriberDid.Should().Be("did:plc:abc");
        log.Details.Should().Be("New subscriber via like.");
        log.Timestamp.Should().BeOnOrAfter(before).And.BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void AuditLog_Create_WithNullDid_AllowsSystemEvents()
    {
        var log = AuditLog.Create(AuditEventType.RescoringRun, null, "Rescore cycle: 42 subscribers");

        log.SubscriberDid.Should().BeNull();
        log.EventType.Should().Be(AuditEventType.RescoringRun);
        log.Details.Should().Be("Rescore cycle: 42 subscribers");
    }

    [Fact]
    public void TrackedImage_Create_WithAlt_SetsHasAltTrue()
    {
        var image = TrackedImage.Create("at://did:plc:abc/app.bsky.feed.post/1", 0, "bafybeiabc123", true);

        image.PostAtUri.Should().Be("at://did:plc:abc/app.bsky.feed.post/1");
        image.Index.Should().Be(0);
        image.BlobCid.Should().Be("bafybeiabc123");
        image.HasAlt.Should().BeTrue();
    }

    [Fact]
    public void TrackedImage_Create_WithoutAlt_SetsHasAltFalse()
    {
        var image = TrackedImage.Create("at://did:plc:abc/app.bsky.feed.post/1", 1, "bafybeiabc456", false);

        image.Index.Should().Be(1);
        image.HasAlt.Should().BeFalse();
    }
}
