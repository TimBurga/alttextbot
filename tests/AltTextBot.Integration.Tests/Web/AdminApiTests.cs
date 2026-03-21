using System.Net;
using System.Net.Http.Json;
using AltTextBot.Domain.Entities;

namespace AltTextBot.Integration.Tests.Web;

public class AdminApiTests(WebAppFactory factory) : IClassFixture<WebAppFactory>
{
    [Fact]
    public async Task GetSubscribers_WithoutApiKey_Returns401()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/admin/subscribers");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetSubscribers_WithWrongApiKey_Returns401()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Admin-Key", "wrong-key");

        var response = await client.GetAsync("/api/admin/subscribers");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetSubscribers_WithCorrectApiKey_Returns200()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Admin-Key", WebAppFactory.TestApiKey);

        var response = await client.GetAsync("/api/admin/subscribers");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        body.GetProperty("items").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Array);
    }

    [Fact]
    public async Task PostRescore_WithoutApiKey_Returns401()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsync("/api/admin/rescore/did:plc:someone", null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PostRescore_KnownDid_WithCorrectApiKey_Returns200()
    {
        var did = "did:plc:admin-api-rescore-1";
        await using var db = factory.CreateBotDbContext();
        db.Subscribers.Add(Subscriber.Create(did, "rescoretest.bsky.social"));
        await db.SaveChangesAsync();

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Admin-Key", WebAppFactory.TestApiKey);

        var response = await client.PostAsync($"/api/admin/rescore/{did}", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        body.GetProperty("did").GetString().Should().Be(did);
    }
}
