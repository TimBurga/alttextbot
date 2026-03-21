using AltTextBot.Application.Configuration;
using AltTextBot.Application.Interfaces;
using AltTextBot.Domain.Entities;
using AltTextBot.Domain.Enums;
using AltTextBot.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AltTextBot.Integration.Tests;

public class RescoringCycleTests(DatabaseFixture fixture) : IClassFixture<DatabaseFixture>
{
    // Mirrors the core of RescoringWorker.RunRescoringCycleAsync using real services + mocked HTTP
    private async Task RunCycle(
        ILabelerClient labelerClient,
        ILabelStateReader labelStateReader,
        IEnumerable<string>? subscriberDids = null,
        CancellationToken ct = default)
    {
        await using var db = fixture.CreateDbContext();
        var scoringService = new ScoringService(db, Options.Create(new ScoringOptions()));
        var postTracking = new PostTrackingService(db);
        var auditLogger = new AuditLogger(db);

        var cutoff = DateTimeOffset.UtcNow.AddDays(-30);
        await postTracking.DeleteOldPostsAsync(cutoff, ct);

        var didSet = subscriberDids?.ToHashSet();
        var subscribers = await db.Subscribers
            .Where(s => s.Status == SubscriberStatus.Active && (didSet == null || didSet.Contains(s.Did)))
            .ToListAsync(ct);

        foreach (var subscriber in subscribers)
        {
            var score = await scoringService.ComputeScoreAsync(subscriber.Did, ct);
            var currentLabel = await labelStateReader.GetCurrentLabelAsync(subscriber.Did, ct);
            var effectiveCurrent = currentLabel ?? LabelTier.None;

            if (score.Tier != effectiveCurrent)
            {
                if (score.Tier != LabelTier.None)
                {
                    await labelerClient.ApplyLabelAsync(subscriber.Did, score.Tier, ct);
                    await auditLogger.LogAsync(AuditEventType.LabelApplied, subscriber.Did,
                        $"Applied label: {score.Tier}", ct);
                }
                if (effectiveCurrent != LabelTier.None)
                {
                    await labelerClient.NegateLabelAsync(subscriber.Did, effectiveCurrent, ct);
                    await auditLogger.LogAsync(AuditEventType.LabelNegated, subscriber.Did,
                        $"Negated label: {effectiveCurrent}", ct);
                }
                await auditLogger.LogAsync(AuditEventType.LabelChanged, subscriber.Did,
                    $"Tier changed: {effectiveCurrent} → {score.Tier}", ct);
            }

            subscriber.RecordScored();
        }

        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync(AuditEventType.RescoringRun, null,
            $"Rescore cycle: {subscribers.Count} subscribers", ct);
    }

    [Fact]
    public async Task RescoringCycle_SubscriberEarnsNewTier_AppliesLabelAndLogsAudit()
    {
        var did = "did:plc:rescore-cycle-earn-1";
        await using var setup = fixture.CreateDbContext();
        setup.Subscribers.Add(Subscriber.Create(did, "earner.bsky.social"));
        // 5/5 posts with alt = 100% = Hero
        for (var i = 0; i < 5; i++)
            setup.TrackedPosts.Add(TrackedPost.Create(
                $"at://{did}/app.bsky.feed.post/{i:000}",
                did, postedAt: null, hasImages: true, imageCount: 1, altTextCount: 1));
        await setup.SaveChangesAsync();

        var labelerClient = Substitute.For<ILabelerClient>();
        var labelStateReader = Substitute.For<ILabelStateReader>();
        labelStateReader.GetCurrentLabelAsync(did, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<LabelTier?>(null)); // no label yet

        await RunCycle(labelerClient, labelStateReader, [did]);

        await labelerClient.Received(1).ApplyLabelAsync(did, LabelTier.Hero, Arg.Any<CancellationToken>());
        await labelerClient.DidNotReceive().NegateLabelAsync(Arg.Any<string>(), Arg.Any<LabelTier>(), Arg.Any<CancellationToken>());

        await using var verify = fixture.CreateDbContext();
        verify.AuditLogs.Should().Contain(l => l.SubscriberDid == did && l.EventType == AuditEventType.LabelApplied);
        verify.AuditLogs.Should().Contain(l => l.EventType == AuditEventType.RescoringRun);

        var subscriber = await verify.Subscribers.FindAsync([did]);
        subscriber!.LastScoredAt.Should().NotBeNull();
    }

    [Fact]
    public async Task RescoringCycle_SubscriberAlreadyAtCorrectTier_DoesNotCallLabeler()
    {
        var did = "did:plc:rescore-cycle-same-1";
        await using var setup = fixture.CreateDbContext();
        setup.Subscribers.Add(Subscriber.Create(did, "steady.bsky.social"));
        // No image posts → None tier
        await setup.SaveChangesAsync();

        var labelerClient = Substitute.For<ILabelerClient>();
        var labelStateReader = Substitute.For<ILabelStateReader>();
        labelStateReader.GetCurrentLabelAsync(did, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<LabelTier?>(null)); // current: None, computed: None

        await RunCycle(labelerClient, labelStateReader, [did]);

        await labelerClient.DidNotReceive().ApplyLabelAsync(Arg.Any<string>(), Arg.Any<LabelTier>(), Arg.Any<CancellationToken>());
        await labelerClient.DidNotReceive().NegateLabelAsync(Arg.Any<string>(), Arg.Any<LabelTier>(), Arg.Any<CancellationToken>());
    }
}
