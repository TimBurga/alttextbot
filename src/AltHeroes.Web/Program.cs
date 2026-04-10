using AltHeroes.Web.Configuration;
using AltHeroes.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
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

// ── Bot admin client ──────────────────────────────────────────────────────────
builder.Services.AddHttpClient<BotAdminClient>((sp, client) =>
{
    var opts = sp.GetRequiredService<IOptions<BotClientOptions>>().Value;
    client.BaseAddress = new Uri(opts.BaseUrl);
});

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

app.Run();
