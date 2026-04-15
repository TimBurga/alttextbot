using System.Security.Cryptography;
using System.Text;
using AltHeroes.Bot;
using AltHeroes.Bot.Configuration;
using AltHeroes.Bot.Data;
using AltHeroes.Bot.Services;
using AltHeroes.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// ── Configuration ────────────────────────────────────────────────────────────
builder.Services.AddOptions<BotOptions>()
    .BindConfiguration(BotOptions.SectionName);

builder.Services.AddOptions<LabelerOptions>()
    .BindConfiguration(LabelerOptions.SectionName)
    .Validate(o => !string.IsNullOrEmpty(o.Did), "Labeler:Did is required.")
    .Validate(o => !string.IsNullOrEmpty(o.OzoneUrl), "Labeler:OzoneUrl is required.")
    .ValidateOnStart();

builder.Services.AddOptions<JetstreamOptions>()
    .BindConfiguration(JetstreamOptions.SectionName);

builder.Services.AddOptions<ScoringOptions>()
    .BindConfiguration(ScoringOptions.SectionName);

builder.Services.AddOptions<AdminOptions>()
    .BindConfiguration(AdminOptions.SectionName)
    .Validate(o => o.ApiKey != "changeme-api-key", "Admin:ApiKey must be changed from default.")
    .ValidateOnStart();

builder.Services.AddOptions<DiscordOptions>()
    .BindConfiguration(DiscordOptions.SectionName);

// ── Database ──────────────────────────────────────────────────────────────────
builder.Services.AddDbContextFactory<BotDbContext>(options =>
{
    var connStr = builder.Configuration.GetConnectionString("BotDb");
    if (string.IsNullOrWhiteSpace(connStr))
        throw new InvalidOperationException("ConnectionStrings:BotDb is required.");
    options.UseNpgsql(connStr);
});

// ── Singletons ────────────────────────────────────────────────────────────────
builder.Services.AddSingleton<BotState>();
builder.Services.AddSingleton<StartupGate>();

// ── HTTP Clients ──────────────────────────────────────────────────────────────
// Named clients for use by singleton services via IHttpClientFactory.
builder.Services.AddHttpClient(nameof(ListRecordsClient));
builder.Services.AddHttpClient(nameof(OzoneClient));
builder.Services.AddHttpClient(nameof(ShutdownNotifierService));

// Singleton services (use IHttpClientFactory internally, safe for long-lived scope)
builder.Services.AddSingleton<ListRecordsClient>();
builder.Services.AddSingleton<OzoneClient>();
if (builder.Environment.IsDevelopment())
    builder.Services.AddSingleton<ICongratsPostService, LogOnlyCongratsPostService>();
else
    builder.Services.AddSingleton<ICongratsPostService, CongratsPostService>();
builder.Services.AddSingleton<LabelDiffService>();

// ── Hosted services ───────────────────────────────────────────────────────────
// BotStartupService runs first (IHostedService ordering is registration order).
// ShutdownNotifierService is registered last so its StopAsync runs after all others.
builder.Services.AddHostedService<BotStartupService>();
builder.Services.AddHostedService<JetstreamWorker>();
builder.Services.AddHostedService<ShutdownNotifierService>();

var app = builder.Build();

// Ensure the database schema exists on startup.
await using (var db = app.Services.GetRequiredService<IDbContextFactory<BotDbContext>>().CreateDbContext())
    await db.Database.EnsureCreatedAsync();

app.MapDefaultEndpoints();

// ── Admin API (protected by API key) ─────────────────────────────────────────

app.MapGet("/admin/subscribers", async (
    BotState state,
    IDbContextFactory<BotDbContext> dbFactory,
    HttpContext ctx,
    IOptions<AdminOptions> adminOpts,
    CancellationToken ct) =>
{
    if (!IsAuthorised(ctx, adminOpts.Value.ApiKey)) return Results.Unauthorized();

    await using var db = dbFactory.CreateDbContext();
    var subscribers = await db.Subscribers.ToListAsync(ct);
    var items = subscribers.Select(s => new
    {
        Did = s.Did,
        Tier = state.GetCurrentTier(s.Did).ToString(),
        Blocked = !s.Active,
    });
    return Results.Ok(new { Count = subscribers.Count, Items = items });
});

app.MapPost("/admin/block/{did}", async (
    string did,
    BotState state,
    IDbContextFactory<BotDbContext> dbFactory,
    HttpContext ctx,
    IOptions<AdminOptions> adminOpts,
    CancellationToken ct) =>
{
    if (!IsAuthorised(ctx, adminOpts.Value.ApiKey)) return Results.Unauthorized();
    if (!state.Contains(did)) return Results.NotFound(new { did, message = "Subscriber not found." });

    await using var db = dbFactory.CreateDbContext();
    var entity = await db.Subscribers.FindAsync([did], ct);
    if (entity is not null)
    {
        entity.Active = false;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    state.UnenrollByDid(did);
    return Results.Ok(new { did, message = "Blocked." });
});

app.MapDelete("/admin/block/{did}", async (
    string did,
    BotState state,
    IDbContextFactory<BotDbContext> dbFactory,
    HttpContext ctx,
    IOptions<AdminOptions> adminOpts,
    CancellationToken ct) =>
{
    if (!IsAuthorised(ctx, adminOpts.Value.ApiKey)) return Results.Unauthorized();

    await using var db = dbFactory.CreateDbContext();
    var entity = await db.Subscribers.FindAsync([did], ct);
    if (entity is null) return Results.NotFound(new { did, message = "Subscriber not found." });

    entity.Active = true;
    entity.UpdatedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync(ct);

    state.Enroll(entity.Did, entity.RKey);
    return Results.Ok(new { did, message = "Unblocked." });
});

app.MapPost("/admin/rescore/{did}", async (
    string did,
    BotState state,
    ListRecordsClient listRecords,
    LabelDiffService diff,
    IOptions<ScoringOptions> scoringOpts,
    HttpContext ctx,
    IOptions<AdminOptions> adminOpts,
    CancellationToken ct) =>
{
    if (!IsAuthorised(ctx, adminOpts.Value.ApiKey)) return Results.Unauthorized();
    if (!state.Contains(did)) return Results.NotFound(new { did, message = "Subscriber not found." });

    var config = scoringOpts.Value.ToConfig();
    var posts = await listRecords.GetPostsAsync(did, config.WindowDays, ct);
    var result = ScoringService.ComputeTier(posts, config, DateTimeOffset.UtcNow);
    await diff.ApplyIfChangedAsync(did, did, result.Tier, ct);
    return Results.Ok(new { did, tier = result.Tier.ToString(), score = result.Score });
});

app.Run();

static bool IsAuthorised(HttpContext ctx, string expectedKey)
{
    var provided = ctx.Request.Headers["X-Admin-Key"].FirstOrDefault();
    if (provided is null) return false;
    return CryptographicOperations.FixedTimeEquals(
        Encoding.UTF8.GetBytes(provided),
        Encoding.UTF8.GetBytes(expectedKey));
}
