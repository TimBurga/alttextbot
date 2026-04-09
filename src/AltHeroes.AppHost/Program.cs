var builder = DistributedApplication.CreateBuilder(args);

var bot = builder.AddProject<Projects.AltHeroes_Bot>("bot")
    .WithEnvironment("Bot__Did", builder.Configuration["Bot:Did"] ?? "")
    .WithEnvironment("Bot__Handle", builder.Configuration["Bot:Handle"] ?? "")
    .WithEnvironment("Bot__AppPassword", builder.Configuration["Bot:AppPassword"] ?? "")
    .WithEnvironment("Labeler__Did", builder.Configuration["Labeler:Did"] ?? "")
    .WithEnvironment("Labeler__OzoneUrl", builder.Configuration["Labeler:OzoneUrl"] ?? "")
    .WithEnvironment("Jetstream__Url", builder.Configuration["Jetstream:Url"] ?? "wss://jetstream2.us-east.bsky.network/subscribe");

builder.Build().Run();
