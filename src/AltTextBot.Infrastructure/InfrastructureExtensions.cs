using AltTextBot.Application.Configuration;
using AltTextBot.Application.Interfaces;
using AltTextBot.Infrastructure.Data;
using AltTextBot.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AltTextBot.Infrastructure;

public static class InfrastructureExtensions
{
    public static IHostApplicationBuilder AddInfrastructure(this IHostApplicationBuilder builder)
    {
        // Bot DB via Aspire integration
        builder.AddNpgsqlDbContext<AltTextBotDbContext>("alttext-bot");

        // Labeler DB (read-only, external connection string — required)
        var labelerConnStr = builder.Configuration["ConnectionStrings:labeler-db"]
            ?? throw new InvalidOperationException(
                "Required connection string 'labeler-db' is not configured. " +
                "Add it to appsettings.json, user secrets, or via Aspire's AddConnectionString(\"labeler-db\").");
        builder.Services.AddDbContext<LabelerDbContext>(options =>
            options.UseNpgsql(labelerConnStr).UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));

        // Configuration
        builder.Services.Configure<ScoringOptions>(
            builder.Configuration.GetSection(ScoringOptions.SectionName));
        builder.Services.Configure<BotOptions>(
            builder.Configuration.GetSection(BotOptions.SectionName));
        builder.Services.Configure<JetstreamOptions>(
            builder.Configuration.GetSection(JetstreamOptions.SectionName));
        builder.Services.Configure<LabelerOptions>(
            builder.Configuration.GetSection(LabelerOptions.SectionName));
        builder.Services.Configure<TapOptions>(
            builder.Configuration.GetSection(TapOptions.SectionName));

        // Subscriber set (singleton, thread-safe)
        builder.Services.AddSingleton<ISubscriberSet, SubscriberSet>();

        // Services (scoped)
        builder.Services.AddScoped<IAuditLogger, AuditLogger>();
        builder.Services.AddScoped<IFirehoseStateRepository, FirehoseStateRepository>();
        builder.Services.AddScoped<IPostTrackingService, PostTrackingService>();
        builder.Services.AddScoped<IScoringService, ScoringService>();
        builder.Services.AddScoped<ILabelStateReader, LabelStateReader>();
        builder.Services.AddScoped<ISubscriberService, SubscriberService>();
        builder.Services.AddScoped<IAdminService, AdminService>();

        // Labeler HTTP client with resilience
        builder.Services.AddHttpClient<ILabelerClient, LabelerHttpClient>((sp, client) =>
        {
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<LabelerOptions>>().Value;
            if (!string.IsNullOrWhiteSpace(options.BaseUrl))
                client.BaseAddress = new Uri(options.BaseUrl);
        }).AddStandardResilienceHandler();

        // Bluesky bot client (authenticated, singleton — maintains session)
        builder.Services.AddSingleton<IBlueskyPostClient, BlueskyBotClient>();

        // Bluesky public API client (shared, no auth)
        builder.Services.AddHttpClient<IBlueskyApiClient, BlueskyApiClient>(client =>
        {
            client.BaseAddress = new Uri("https://public.api.bsky.app");
            client.DefaultRequestHeaders.Add("Accept", "application/json");
        }).AddStandardResilienceHandler();

        // Tap sidecar HTTP client
        builder.Services.AddHttpClient<ITapApiClient, TapApiClient>((sp, client) =>
        {
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<TapOptions>>().Value;
            if (!string.IsNullOrWhiteSpace(options.BaseUrl))
                client.BaseAddress = new Uri(options.BaseUrl);
        }).AddStandardResilienceHandler();

        return builder;
    }

    public static async Task ApplyMigrationsAsync(this IHost host)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AltTextBotDbContext>();
        await db.Database.MigrateAsync();
    }
}
