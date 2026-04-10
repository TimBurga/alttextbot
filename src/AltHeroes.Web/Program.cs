using AltHeroes.Web.Configuration;
using AltHeroes.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// ── Configuration ─────────────────────────────────────────────────────────────
builder.Services.AddOptions<AdminOptions>()
    .BindConfiguration(AdminOptions.SectionName)
    .Validate(o => o.Password != "changeme", "Admin:Password must be changed from default.")
    .ValidateOnStart();

builder.Services.AddOptions<BotClientOptions>()
    .BindConfiguration(BotClientOptions.SectionName);

builder.Services.AddOptions<ScoringOptions>()
    .BindConfiguration(ScoringOptions.SectionName);

// ── HTTP clients ──────────────────────────────────────────────────────────────
builder.Services.AddHttpClient<BotAdminClient>((sp, client) =>
{
    var opts = sp.GetRequiredService<IOptions<BotClientOptions>>().Value;
    client.BaseAddress = new Uri(opts.BaseUrl);
});

builder.Services.AddHttpClient(nameof(HandleResolver));
builder.Services.AddHttpClient(nameof(ScoringStreamService));

// ── Public scoring services ───────────────────────────────────────────────────
builder.Services.AddSingleton<HandleResolver>();
builder.Services.AddSingleton<ScoringStreamService>();

// ── Auth ──────────────────────────────────────────────────────────────────────
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/admin/login";
        options.LogoutPath = "/admin/logout";
        options.Cookie.Name = "AltHeroes.Admin";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    });

builder.Services.AddAuthorization();
builder.Services.AddRazorPages();

var app = builder.Build();

app.MapDefaultEndpoints();

// Trust the X-Forwarded-Proto header from Caddy so ASP.NET Core knows
// the original request was HTTPS (needed for cookie Secure flag + redirects).
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapRazorPages();

// ── Admin logout ──────────────────────────────────────────────────────────────
app.MapPost("/admin/logout", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/admin/login");
}).RequireAuthorization();

// ── Admin API pass-through (AJAX endpoints called from the admin page) ────────
// These are authenticated via the web session cookie; the Bot's API key never
// leaves the server.

app.MapPost("/admin/api/block/{did}", async (string did, BotAdminClient bot, HttpContext ctx, CancellationToken ct) =>
{
    if (!ctx.User.Identity?.IsAuthenticated ?? true) return Results.Unauthorized();
    var ok = await bot.BlockAsync(did, ct);
    return ok ? Results.Ok() : Results.Problem("Bot returned an error.");
}).RequireAuthorization();

app.MapDelete("/admin/api/block/{did}", async (string did, BotAdminClient bot, HttpContext ctx, CancellationToken ct) =>
{
    if (!ctx.User.Identity?.IsAuthenticated ?? true) return Results.Unauthorized();
    var ok = await bot.UnblockAsync(did, ct);
    return ok ? Results.Ok() : Results.Problem("Bot returned an error.");
}).RequireAuthorization();

app.MapPost("/admin/api/rescore/{did}", async (string did, BotAdminClient bot, HttpContext ctx, CancellationToken ct) =>
{
    if (!ctx.User.Identity?.IsAuthenticated ?? true) return Results.Unauthorized();
    var result = await bot.RescoreAsync(did, ct);
    return result is not null ? Results.Ok(result) : Results.Problem("Bot returned an error.");
}).RequireAuthorization();

// ── Public SSE scoring stream ─────────────────────────────────────────────────
app.MapGet("/stream/{did}", async (string did, ScoringStreamService scorer, HttpContext ctx, CancellationToken ct) =>
{
    ctx.Response.ContentType = "text/event-stream";
    ctx.Response.Headers.CacheControl = "no-cache";
    ctx.Response.Headers["X-Accel-Buffering"] = "no";

    await foreach (var evt in scorer.StreamAsync(did, ct))
    {
        var line = evt switch
        {
            ImageEvent e =>
                $"event: image\ndata: {{\"date\":\"{e.Date}\"}}\n\n",
            DayCompleteEvent e =>
                $"event: day_complete\ndata: {{\"date\":\"{e.Date}\",\"allCompliant\":{(e.AllCompliant ? "true" : "false")}}}\n\n",
            CalculatingEvent =>
                "event: calculating\ndata: {}\n\n",
            DoneEvent e =>
                $"event: done\ndata: {{\"tier\":\"{e.Tier}\",\"score\":{e.Score:F1},\"totalImagePosts\":{e.TotalImagePosts},\"compliantPosts\":{e.CompliantPosts}}}\n\n",
            _ => ""
        };

        if (line.Length == 0) continue;
        await ctx.Response.WriteAsync(line, ct);
        await ctx.Response.Body.FlushAsync(ct);
    }
});

app.Run();
