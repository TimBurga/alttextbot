using System.Net;
using System.Net.Http.Json;
using AltTextBot.Domain.Entities;

namespace AltTextBot.Integration.Tests.Web;

public class StatsApiTests(WebAppFactory factory) : IClassFixture<WebAppFactory>
{
    [Fact]
    public async Task GetStats_UnknownHandle_Returns404()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/users/nobody.bsky.social/stats");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetStats_KnownHandle_Returns200WithStats()
    {
        var handle = "statstest.bsky.social";
        var did = "did:plc:stats-api-1";
        await using var db = factory.CreateBotDbContext();
        db.Subscribers.Add(Subscriber.Create(did, handle));
        await db.SaveChangesAsync();

        var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/users/{handle}/stats");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        body.GetProperty("did").GetString().Should().Be(did);
        body.GetProperty("handle").GetString().Should().Be(handle);
        body.GetProperty("tier").GetString().Should().Be("None");
    }
}
