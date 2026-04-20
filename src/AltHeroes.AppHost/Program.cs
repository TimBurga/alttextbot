var builder = DistributedApplication.CreateBuilder(args);

var adminApiKey = builder.Configuration["Admin:ApiKey"] ?? "changeme-api-key";

string GetConfig(string key, string defaultValue = "") =>
    builder.Configuration[key] ?? defaultValue;

var bot = builder.AddProject<Projects.AltHeroes_Bot>("bot")
    .WithEnvironment("Bot__Did", GetConfig("Bot:Did"))
    .WithEnvironment("Bot__Handle", GetConfig("Bot:Handle"))
    .WithEnvironment("Bot__AppPassword", GetConfig("Bot:AppPassword"))
    .WithEnvironment("Labeler__Did", GetConfig("Labeler:Did"))
    .WithEnvironment("Labeler__OzoneUrl", GetConfig("Labeler:OzoneUrl"))
    .WithEnvironment("Jetstream__Url", GetConfig("Jetstream:Url", "wss://jetstream2.us-east.bsky.network/subscribe"))
    .WithEnvironment("Admin__ApiKey", adminApiKey);

var web = builder.AddProject<Projects.AltHeroes_Web>("web")
    .WithReference(bot)
    .WithEnvironment("Admin__Password", GetConfig("Admin:Password"))
    .WithEnvironment("BotClient__AdminApiKey", adminApiKey)
    .WithExternalHttpEndpoints();

builder.Build().Run();
