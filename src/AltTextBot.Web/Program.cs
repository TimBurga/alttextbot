using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using AltTextBot.Application.Interfaces;
using AltTextBot.Infrastructure;
using AltTextBot.Infrastructure.Data;
using AltTextBot.Web.Configuration;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddInfrastructure();

builder.Services.AddOptions<AdminOptions>()
    .BindConfiguration(AdminOptions.SectionName)
    .Validate(
        o => o.Password != "changeme" && o.ApiKey != "changeme-api-key",
        "Admin:Password and Admin:ApiKey must be changed from their default values.")
    .ValidateOnStart();

builder.Services.AddRazorPages();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/admin/login";
        options.LogoutPath = "/admin/logout";
        options.Cookie.Name = "AltTextBot.Admin";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
    });

builder.Services.AddAuthorization();

builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("login", httpContext =>
    {
        var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        // Only rate-limit POST requests so the redirect GET for the 429 message is never blocked
        return httpContext.Request.Method == HttpMethods.Post
            ? RateLimitPartition.GetFixedWindowLimiter($"{ip}:POST", _ => new FixedWindowRateLimiterOptions
                { PermitLimit = 5, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })
            : RateLimitPartition.GetNoLimiter(ip);
    });
    options.OnRejected = (context, _) =>
    {
        context.HttpContext.Response.Redirect("/admin/login?rateLimited=1");
        return ValueTask.CompletedTask;
    };
});

var app = builder.Build();

app.MapDefaultEndpoints();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapRazorPages();

// Minimal API endpoints
app.MapGet("/api/users/{handle}/stats", async (
    string handle,
    AltTextBotDbContext db,
    IScoringService scoring,
    ILabelStateReader labelReader,
    CancellationToken ct) =>
{
    var subscriber = await db.Subscribers.FirstOrDefaultAsync(s => s.Handle == handle, ct);
    if (subscriber is null) return Results.NotFound();

    var score = await scoring.ComputeScoreAsync(subscriber.Did, ct);
    var tier = await labelReader.GetCurrentLabelAsync(subscriber.Did, ct);

    return Results.Ok(new
    {
        subscriber.Did,
        subscriber.Handle,
        Score = score.ScorePercent,
        Tier = tier?.ToString() ?? "None",
        score.TotalImagePosts,
        score.PostsWithAllAlt,
        subscriber.LastScoredAt
    });
});

app.MapGet("/api/admin/subscribers", async (
    IAdminService adminService,
    HttpContext ctx,
    IOptions<AdminOptions> adminOptions,
    CancellationToken ct,
    int page = 1) =>
{
    var key = ctx.Request.Headers["X-Admin-Key"].FirstOrDefault();
    if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(key ?? ""), Encoding.UTF8.GetBytes(adminOptions.Value.ApiKey))) return Results.Unauthorized();
    var result = await adminService.GetSubscribersAsync(page, 20, ct);
    return Results.Ok(result);
});

app.MapPost("/api/admin/rescore/{did}", async (
    string did,
    IAdminService adminService,
    HttpContext ctx,
    IOptions<AdminOptions> adminOptions,
    CancellationToken ct) =>
{
    var key = ctx.Request.Headers["X-Admin-Key"].FirstOrDefault();
    if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(key ?? ""), Encoding.UTF8.GetBytes(adminOptions.Value.ApiKey))) return Results.Unauthorized();
    await adminService.ManualRescoreAsync(did, ct);
    return Results.Ok(new { did, message = "Rescore triggered" });
});

app.Run();

public partial class Program { }
