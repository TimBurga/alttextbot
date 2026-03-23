using AltTextBot.Application.Interfaces;
using AltTextBot.Domain.Enums;
using AltTextBot.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute.ExceptionExtensions;

namespace AltTextBot.Integration.Tests;

public class SubscriberServiceTests(DatabaseFixture fixture) : IClassFixture<DatabaseFixture>
{
    private SubscriberService CreateService(AltTextBot.Infrastructure.Data.AltTextBotDbContext db)
    {
        var subscriberSet = new SubscriberSet();
        var auditLogger = new AuditLogger(db);
        var tapClient = Substitute.For<ITapApiClient>();
        return new SubscriberService(db, subscriberSet, auditLogger, tapClient, NullLogger<SubscriberService>.Instance);
    }

    [Fact]
    public async Task SubscribeAsync_NewSubscriber_CreatesSubscriberAndAuditLog()
    {
        await using var db = fixture.CreateDbContext();
        var service = CreateService(db);
        var did = "did:plc:sub-new-1";

        await service.SubscribeAsync(did, "newuser.bsky.social");

        await using var verify = fixture.CreateDbContext();
        var subscriber = await verify.Subscribers.FindAsync([did]);
        subscriber.Should().NotBeNull();
        subscriber!.Handle.Should().Be("newuser.bsky.social");
        subscriber.Status.Should().Be(SubscriberStatus.Active);

        var log = await verify.AuditLogs.FirstOrDefaultAsync(l => l.SubscriberDid == did && l.EventType == AuditEventType.SubscriberAdded);
        log.Should().NotBeNull();
    }

    [Fact]
    public async Task SubscribeAsync_AlreadyActive_IsIdempotent()
    {
        await using var db = fixture.CreateDbContext();
        var service = CreateService(db);
        var did = "did:plc:sub-dupe-1";
        await service.SubscribeAsync(did, "dupuser.bsky.social");

        await service.SubscribeAsync(did, "dupuser.bsky.social");

        await using var verify = fixture.CreateDbContext();
        var count = await verify.Subscribers.CountAsync(s => s.Did == did);
        count.Should().Be(1);
        var logCount = await verify.AuditLogs.CountAsync(l => l.SubscriberDid == did && l.EventType == AuditEventType.SubscriberAdded);
        logCount.Should().Be(1);
    }

    [Fact]
    public async Task SubscribeAsync_Deactivated_ReactivatesAndLogsAudit()
    {
        await using var db = fixture.CreateDbContext();
        var service = CreateService(db);
        var did = "did:plc:sub-react-1";
        await service.SubscribeAsync(did, "reactuser.bsky.social");
        await service.UnsubscribeAsync(did);

        await using var db2 = fixture.CreateDbContext();
        var service2 = CreateService(db2);
        await service2.SubscribeAsync(did, "reactuser.bsky.social");

        await using var verify = fixture.CreateDbContext();
        var subscriber = await verify.Subscribers.FindAsync([did]);
        subscriber!.Status.Should().Be(SubscriberStatus.Active);
        var reactivatedLog = await verify.AuditLogs.FirstOrDefaultAsync(l => l.SubscriberDid == did && l.EventType == AuditEventType.SubscriberReactivated);
        reactivatedLog.Should().NotBeNull();
    }

    [Fact]
    public async Task UnsubscribeAsync_ActiveSubscriber_DeactivatesAndLogsAudit()
    {
        await using var db = fixture.CreateDbContext();
        var service = CreateService(db);
        var did = "did:plc:sub-unsub-1";
        await service.SubscribeAsync(did, "unsubuser.bsky.social");

        await using var db2 = fixture.CreateDbContext();
        var service2 = CreateService(db2);
        await service2.UnsubscribeAsync(did);

        await using var verify = fixture.CreateDbContext();
        var subscriber = await verify.Subscribers.FindAsync([did]);
        subscriber!.Status.Should().Be(SubscriberStatus.Deactivated);
        var log = await verify.AuditLogs.FirstOrDefaultAsync(l => l.SubscriberDid == did && l.EventType == AuditEventType.SubscriberDeactivated);
        log.Should().NotBeNull();
    }

    [Fact]
    public async Task UnsubscribeAsync_NonExistent_DoesNothing()
    {
        await using var db = fixture.CreateDbContext();
        var service = CreateService(db);

        var act = async () => await service.UnsubscribeAsync("did:plc:ghost-1");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SubscribeAsync_WhenTapThrows_SubscriberIsStillCreated()
    {
        await using var db = fixture.CreateDbContext();
        var subscriberSet = new SubscriberSet();
        var auditLogger = new AuditLogger(db);
        var tapClient = Substitute.For<ITapApiClient>();
        tapClient.AddReposAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Tap service unavailable"));

        var service = new SubscriberService(db, subscriberSet, auditLogger, tapClient, NullLogger<SubscriberService>.Instance);
        var did = "did:plc:tap-fail-test-1";

        await service.SubscribeAsync(did, "tapfail.bsky.social");

        await using var verify = fixture.CreateDbContext();
        var subscriber = await verify.Subscribers.FindAsync([did]);
        subscriber.Should().NotBeNull("subscriber should be persisted even when Tap registration fails");
        subscriber!.Status.Should().Be(SubscriberStatus.Active);
    }

    [Fact]
    public async Task GetActiveSubscriberDidsAsync_ReturnsOnlyActiveDids()
    {
        await using var db = fixture.CreateDbContext();
        var service = CreateService(db);
        var activeDid = "did:plc:sub-active-filter-1";
        var inactiveDid = "did:plc:sub-inactive-filter-1";
        await service.SubscribeAsync(activeDid, "active.bsky.social");
        await service.SubscribeAsync(inactiveDid, "inactive.bsky.social");

        await using var db2 = fixture.CreateDbContext();
        var service2 = CreateService(db2);
        await service2.UnsubscribeAsync(inactiveDid);

        await using var db3 = fixture.CreateDbContext();
        var service3 = CreateService(db3);
        var dids = await service3.GetActiveSubscriberDidsAsync();

        dids.Should().Contain(activeDid);
        dids.Should().NotContain(inactiveDid);
    }
}
