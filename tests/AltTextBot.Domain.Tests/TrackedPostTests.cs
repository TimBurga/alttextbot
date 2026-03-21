using AltTextBot.Domain.Entities;
using FluentAssertions;

namespace AltTextBot.Domain.Tests;

public class TrackedPostTests
{
    [Fact]
    public void Create_WithAllAlt_SetsAllImagesHaveAltTrue()
    {
        var post = TrackedPost.Create("at://did:plc:abc/app.bsky.feed.post/123", "did:plc:abc", null, true, 3, 3);

        post.AllImagesHaveAlt.Should().BeTrue();
        post.HasImages.Should().BeTrue();
        post.ImageCount.Should().Be(3);
        post.AltTextCount.Should().Be(3);
    }

    [Fact]
    public void Create_WithMissingAlt_SetsAllImagesHaveAltFalse()
    {
        var post = TrackedPost.Create("at://did:plc:abc/app.bsky.feed.post/123", "did:plc:abc", null, true, 3, 2);

        post.AllImagesHaveAlt.Should().BeFalse();
        post.AltTextCount.Should().Be(2);
    }

    [Fact]
    public void Create_WithNoImages_SetsAllImagesHaveAltFalse()
    {
        var post = TrackedPost.Create("at://did:plc:abc/app.bsky.feed.post/123", "did:plc:abc", null, false, 0, 0);

        post.HasImages.Should().BeFalse();
        post.AllImagesHaveAlt.Should().BeFalse();
    }
}
