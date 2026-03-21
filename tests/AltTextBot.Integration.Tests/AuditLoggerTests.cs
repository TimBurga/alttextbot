using AltTextBot.Domain.Enums;
using AltTextBot.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace AltTextBot.Integration.Tests;

public class AuditLoggerTests(DatabaseFixture fixture) : IClassFixture<DatabaseFixture>
{
    [Fact]
    public async Task LogAsync_PersistsEventToDatabase()
    {
        await using var db = fixture.CreateDbContext();
        var logger = new AuditLogger(db);
        var did = "did:plc:audit-persist-1";

        await logger.LogAsync(AuditEventType.SubscriberAdded, did, "test details");

        await using var verify = fixture.CreateDbContext();
        var log = await verify.AuditLogs.FirstOrDefaultAsync(l => l.SubscriberDid == did);
        log.Should().NotBeNull();
        log!.EventType.Should().Be(AuditEventType.SubscriberAdded);
        log.Details.Should().Be("test details");
        log.Timestamp.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task LogAsync_WithNullDid_PersistsSystemEvent()
    {
        await using var db = fixture.CreateDbContext();
        var logger = new AuditLogger(db);
        var uniqueDetails = $"cycle complete {Guid.NewGuid()}";

        await logger.LogAsync(AuditEventType.RescoringRun, null, uniqueDetails);

        await using var verify = fixture.CreateDbContext();
        var log = await verify.AuditLogs.FirstOrDefaultAsync(l => l.Details == uniqueDetails);
        log.Should().NotBeNull();
        log!.SubscriberDid.Should().BeNull();
        log.EventType.Should().Be(AuditEventType.RescoringRun);
    }

    [Fact]
    public async Task LogAsync_MultipleEvents_AllPersisted()
    {
        await using var db = fixture.CreateDbContext();
        var logger = new AuditLogger(db);
        var did = "did:plc:audit-multi-1";

        await logger.LogAsync(AuditEventType.SubscriberAdded, did, "added");
        await logger.LogAsync(AuditEventType.LabelApplied, did, "bronze applied");
        await logger.LogAsync(AuditEventType.LabelChanged, did, "bronze → silver");

        await using var verify = fixture.CreateDbContext();
        var logs = await verify.AuditLogs.Where(l => l.SubscriberDid == did).ToListAsync();
        logs.Should().HaveCount(3);
    }
}
