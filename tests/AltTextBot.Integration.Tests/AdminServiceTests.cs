using AltTextBot.Application.Configuration;
using AltTextBot.Application.Interfaces;
using AltTextBot.Domain.Enums;
using AltTextBot.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AltTextBot.Integration.Tests;

public class AdminServiceTests(DatabaseFixture fixture) : IClassFixture<DatabaseFixture>
{
    private AdminService CreateService(
        AltTextBot.Infrastructure.Data.AltTextBotDbContext db,
        ILabelerClient? labelerClient = null,
        ILabelStateReader? labelStateReader = null)
    {
        var scoring = new ScoringService(db, Options.Create(new ScoringOptions()));
        var auditLogger = new AuditLogger(db);
        return new AdminService(
            db,
            scoring,
            labelerClient ?? Substitute.For<ILabelerClient>(),
            labelStateReader ?? Substitute.For<ILabelStateReader>(),
            auditLogger,
            Substitute.For<IBlueskyPostClient>(),
            NullLogger<AdminService>.Instance);
    }

    private async Task<string> SeedActiveSubscriber(string did, string handle = "test.bsky.social")
    {
        await using var db = fixture.CreateDbContext();
        var subscriber = AltTextBot.Domain.Entities.Subscriber.Create(did, handle);
        db.Subscribers.Add(subscriber);
        await db.SaveChangesAsync();
        return did;
    }

    [Fact]
    public async Task GetSubscribersAsync_ReturnsPagedResultsNewestFirst()
    {
        var prefix = $"did:plc:admin-page-{Guid.NewGuid():N}";
        var did1 = $"{prefix}-a";
        var did2 = $"{prefix}-b";

        await using var setup = fixture.CreateDbContext();
        setup.Subscribers.Add(AltTextBot.Domain.Entities.Subscriber.Create(did1, "user1.bsky.social"));
        await setup.SaveChangesAsync();
        await Task.Delay(10); // ensure distinct SubscribedAt
        setup.Subscribers.Add(AltTextBot.Domain.Entities.Subscriber.Create(did2, "user2.bsky.social"));
        await setup.SaveChangesAsync();

        await using var db = fixture.CreateDbContext();
        var service = CreateService(db);

        var result = await service.GetSubscribersAsync(1, 20);

        var seeded = result.Items.Where(s => s.Did.StartsWith(prefix)).ToList();
        seeded.Should().HaveCount(2);
        seeded[0].Did.Should().Be(did2); // newest first
        seeded[1].Did.Should().Be(did1);
    }

    [Fact]
    public async Task GetSubscriberAsync_UnknownDid_ReturnsNull()
    {
        await using var db = fixture.CreateDbContext();
        var service = CreateService(db);

        var result = await service.GetSubscriberAsync("did:plc:nobody");

        result.Should().BeNull();
    }

    [Fact]
    public async Task ManualRescoreAsync_UnknownDid_DoesNothing()
    {
        await using var db = fixture.CreateDbContext();
        var service = CreateService(db);

        var act = async () => await service.ManualRescoreAsync("did:plc:nobody-rescore");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ManualRescoreAsync_WhenTierUnchanged_DoesNotCallLabeler()
    {
        var did = "did:plc:admin-rescore-same-1";
        await SeedActiveSubscriber(did);

        var labelerClient = Substitute.For<ILabelerClient>();
        var labelStateReader = Substitute.For<ILabelStateReader>();
        labelStateReader.GetCurrentLabelAsync(did, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<LabelTier?>(null)); // current: None
        // No posts → computed tier is also None

        await using var db = fixture.CreateDbContext();
        var service = CreateService(db, labelerClient, labelStateReader);

        await service.ManualRescoreAsync(did);

        await labelerClient.DidNotReceive().ApplyLabelAsync(Arg.Any<string>(), Arg.Any<LabelTier>(), Arg.Any<CancellationToken>());
        await labelerClient.DidNotReceive().NegateLabelAsync(Arg.Any<string>(), Arg.Any<LabelTier>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ManualRescoreAsync_WhenTierChanged_AppliesNewAndNegatesOld()
    {
        var did = "did:plc:admin-rescore-change-1";
        await SeedActiveSubscriber(did);

        // Seed enough posts for Gold (80%+): 5/5 with alt
        await using var postSetup = fixture.CreateDbContext();
        for (var i = 0; i < 5; i++)
        {
            postSetup.TrackedPosts.Add(AltTextBot.Domain.Entities.TrackedPost.Create(
                $"at://did:plc:admin-rescore-change-1/app.bsky.feed.post/{i:000}",
                did, postedAt: null, hasImages: true, imageCount: 1, altTextCount: 1));
        }
        await postSetup.SaveChangesAsync();

        var labelerClient = Substitute.For<ILabelerClient>();
        var labelStateReader = Substitute.For<ILabelStateReader>();
        labelStateReader.GetCurrentLabelAsync(did, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<LabelTier?>(LabelTier.Bronze)); // was Bronze

        await using var db = fixture.CreateDbContext();
        var service = CreateService(db, labelerClient, labelStateReader);

        await service.ManualRescoreAsync(did);

        // Score is 100% → Hero; old label was Bronze
        await labelerClient.Received(1).ApplyLabelAsync(did, LabelTier.Hero, Arg.Any<CancellationToken>());
        await labelerClient.Received(1).NegateLabelAsync(did, LabelTier.Bronze, Arg.Any<CancellationToken>());

        // Audit log for LabelChanged and ManualRescore written
        await using var verify = fixture.CreateDbContext();
        var logs = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(
            verify.AuditLogs.Where(l => l.SubscriberDid == did));
        logs.Should().Contain(l => l.EventType == AuditEventType.LabelChanged);
        logs.Should().Contain(l => l.EventType == AuditEventType.ManualRescore);
    }
}
