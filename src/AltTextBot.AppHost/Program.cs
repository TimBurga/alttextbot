var builder = DistributedApplication.CreateBuilder(args);

// Bot's own database
var botPostgres = builder.AddPostgres("postgres")
    .WithPgAdmin()
    .AddDatabase("alttext-bot");

// Labeler database (read-only, external — connection string from config/secrets)
var labelerDb = builder.AddConnectionString("labeler-db");

// Tap: ATProto sync and backfill sidecar
var tap = builder.AddContainer("tap", "ghcr.io/bluesky-social/indigo/tap")
    .WithImageTag("latest")
    .WithHttpEndpoint(port: 2480, targetPort: 2480, name: "http")
    .WithEnvironment("TAP_COLLECTION_FILTERS", "app.bsky.feed.post");

var worker = builder.AddProject<Projects.AltTextBot_Worker>("worker")
    .WithReference(botPostgres)
    .WithReference(labelerDb)
    .WithEnvironment("Bot__Did", builder.Configuration["Bot:Did"] ?? "")
    .WithEnvironment("Bot__Handle", builder.Configuration["Bot:Handle"] ?? "")
    .WithEnvironment("Bot__AppPassword", builder.Configuration["Bot:AppPassword"] ?? "")
    .WithEnvironment("Jetstream__Url", builder.Configuration["Jetstream:Url"] ?? "wss://jetstream2.us-east.bsky.network/subscribe")
    .WithEnvironment("Labeler__BaseUrl", builder.Configuration["Labeler:BaseUrl"] ?? "")
    .WithEnvironment("Tap__BaseUrl", tap.GetEndpoint("http"));

var web = builder.AddProject<Projects.AltTextBot_Web>("web")
    .WithReference(botPostgres)
    .WithReference(labelerDb)
    .WithExternalHttpEndpoints();

builder.Build().Run();
