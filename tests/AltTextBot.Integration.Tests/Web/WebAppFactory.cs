using AltTextBot.Application.Interfaces;
using AltTextBot.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace AltTextBot.Integration.Tests.Web;

public sealed class WebAppFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _botContainer = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .Build();

    private readonly PostgreSqlContainer _labelerContainer = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .Build();

    public const string TestApiKey = "test-admin-key-xyz";

    public ILabelerClient LabelerClient { get; } = Substitute.For<ILabelerClient>();
    public IBlueskyApiClient BlueskyClient { get; } = Substitute.For<IBlueskyApiClient>();
    public ITapApiClient TapClient { get; } = Substitute.For<ITapApiClient>();
    public IBlueskyPostClient PostClient { get; } = Substitute.For<IBlueskyPostClient>();

    public AltTextBotDbContext CreateBotDbContext()
    {
        var options = new DbContextOptionsBuilder<AltTextBotDbContext>()
            .UseNpgsql(_botContainer.GetConnectionString())
            .Options;
        return new AltTextBotDbContext(options);
    }

    public LabelerDbContext CreateLabelerDbContext()
    {
        var options = new DbContextOptionsBuilder<LabelerDbContext>()
            .UseNpgsql(_labelerContainer.GetConnectionString())
            .Options;
        return new LabelerDbContext(options);
    }

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_botContainer.StartAsync(), _labelerContainer.StartAsync());

        await using var botDb = CreateBotDbContext();
        await botDb.Database.MigrateAsync();

        await using var labelerDb = CreateLabelerDbContext();
        await labelerDb.Database.EnsureCreatedAsync();
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await Task.WhenAll(_botContainer.DisposeAsync().AsTask(), _labelerContainer.DisposeAsync().AsTask());
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:alttext-bot", _botContainer.GetConnectionString());
        builder.UseSetting("ConnectionStrings:labeler-db", _labelerContainer.GetConnectionString());
        builder.UseSetting("Admin:ApiKey", TestApiKey);
        builder.UseSetting("Admin:Password", "test-password-xyz");
        builder.UseSetting("Bot:Did", "did:plc:testbot");
        builder.UseSetting("Bot:Handle", "testbot.bsky.social");

        builder.ConfigureServices(services =>
        {
            // Replace external HTTP clients with test doubles
            var labelerDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(ILabelerClient));
            if (labelerDescriptor is not null) services.Remove(labelerDescriptor);
            services.AddSingleton(LabelerClient);

            var blueskyDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IBlueskyApiClient));
            if (blueskyDescriptor is not null) services.Remove(blueskyDescriptor);
            services.AddSingleton(BlueskyClient);

            var tapDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(ITapApiClient));
            if (tapDescriptor is not null) services.Remove(tapDescriptor);
            services.AddSingleton(TapClient);

            var postClientDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IBlueskyPostClient));
            if (postClientDescriptor is not null) services.Remove(postClientDescriptor);
            services.AddSingleton(PostClient);
        });
    }
}
