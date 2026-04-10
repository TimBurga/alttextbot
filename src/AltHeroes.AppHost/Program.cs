var builder = DistributedApplication.CreateBuilder(args);

var adminApiKey = builder.Configuration["Admin:ApiKey"] ?? "changeme-api-key";

var bot = builder.AddProject<Projects.AltHeroes_Bot>("bot")
    .WithEnvironment("Bot__Did", builder.Configuration["Bot:Did"] ?? "")
    .WithEnvironment("Bot__Handle", builder.Configuration["Bot:Handle"] ?? "")
    .WithEnvironment("Bot__AppPassword", builder.Configuration["Bot:AppPassword"] ?? "")
    .WithEnvironment("Labeler__Did", builder.Configuration["Labeler:Did"] ?? "")
    .WithEnvironment("Labeler__OzoneUrl", builder.Configuration["Labeler:OzoneUrl"] ?? "")
    .WithEnvironment("Jetstream__Url", builder.Configuration["Jetstream:Url"] ?? "wss://jetstream2.us-east.bsky.network/subscribe")
    .WithEnvironment("Admin__ApiKey", adminApiKey);

var web = builder.AddProject<Projects.AltHeroes_Web>("web")
    .WithReference(bot)
    .WithEnvironment("Admin__Password", builder.Configuration["Admin:Password"] ?? "")
    .WithEnvironment("BotClient__AdminApiKey", adminApiKey)
    .WithExternalHttpEndpoints();

builder.Build().Run();
