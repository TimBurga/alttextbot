using System.Net;
using System.Text;
using AltHeroes.Core;
using FluentAssertions;

namespace AltHeroes.Core.Tests;

public class DidResolverTests
{
    private const string StandardResponse = """
        {"service":[{"id":"#atproto_pds","type":"AtprotoPersonalDataServer","serviceEndpoint":"https://pds.example.com"}]}
        """;

    private static HttpClient MakeClient(string responseJson, Action<HttpRequestMessage>? onRequest = null) =>
        new(new FakeHandler(responseJson, onRequest));

    [Fact]
    public async Task PlcDid_ResolvesToPdsEndpoint()
    {
        var result = await DidResolver.ResolvePdsAsync("did:plc:abc123", MakeClient(StandardResponse));
        result.Should().Be("https://pds.example.com");
    }

    [Fact]
    public async Task PlcDid_UsesPlcDirectoryUrl()
    {
        Uri? requestUri = null;
        await DidResolver.ResolvePdsAsync("did:plc:abc123", MakeClient(StandardResponse, req => requestUri = req.RequestUri));
        requestUri!.AbsoluteUri.Should().Be("https://plc.directory/did%3Aplc%3Aabc123");
    }

    [Fact]
    public async Task WebDid_UsesWellKnownUrl()
    {
        Uri? requestUri = null;
        await DidResolver.ResolvePdsAsync("did:web:example.com", MakeClient(StandardResponse, req => requestUri = req.RequestUri));
        requestUri!.AbsoluteUri.Should().Be("https://example.com/.well-known/did.json");
    }

    [Fact]
    public async Task TrailingSlash_IsStrippedFromEndpoint()
    {
        const string json = """{"service":[{"id":"#atproto_pds","serviceEndpoint":"https://pds.example.com/"}]}""";
        var result = await DidResolver.ResolvePdsAsync("did:plc:abc123", MakeClient(json));
        result.Should().Be("https://pds.example.com");
    }

    [Fact]
    public async Task FullyQualifiedServiceId_IsAccepted()
    {
        // Some DID documents use the full DID as a prefix, e.g. "did:plc:abc#atproto_pds"
        const string json = """{"service":[{"id":"did:plc:abc123#atproto_pds","serviceEndpoint":"https://pds.example.com"}]}""";
        var result = await DidResolver.ResolvePdsAsync("did:plc:abc123", MakeClient(json));
        result.Should().Be("https://pds.example.com");
    }

    [Fact]
    public async Task NoPdsService_Throws()
    {
        const string json = """{"service":[{"id":"#other","serviceEndpoint":"https://other.example.com"}]}""";
        var act = () => DidResolver.ResolvePdsAsync("did:plc:abc123", MakeClient(json));
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*#atproto_pds*");
    }

    [Fact]
    public async Task EmptyServiceArray_Throws()
    {
        const string json = """{"service":[]}""";
        var act = () => DidResolver.ResolvePdsAsync("did:plc:abc123", MakeClient(json));
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task MissingServiceProperty_Throws()
    {
        const string json = """{"id":"did:plc:abc123"}""";
        var act = () => DidResolver.ResolvePdsAsync("did:plc:abc123", MakeClient(json));
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    private sealed class FakeHandler(string json, Action<HttpRequestMessage>? onRequest = null) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            onRequest?.Invoke(request);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }
}
