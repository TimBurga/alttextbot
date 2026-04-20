using System.Net.Http.Json;
using System.Text.Json;

namespace AltHeroes.Core;

public static class DidResolver
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public record DidDocumentInfo(string Pds, string? Handle);

    /// <summary>
    /// Fetches the DID document once and returns both the PDS endpoint and handle.
    /// did:plc → plc.directory; did:web → /.well-known/did.json on the domain.
    /// Throws <see cref="InvalidOperationException"/> if no #atproto_pds service is found.
    /// </summary>
    public static async Task<DidDocumentInfo> ResolveAsync(string did, HttpClient http, CancellationToken ct = default)
    {
        var didDocUrl = did.StartsWith("did:web:")
            ? $"https://{did["did:web:".Length..]}/.well-known/did.json"
            : $"https://plc.directory/{Uri.EscapeDataString(did)}";

        var doc = await http.GetFromJsonAsync<JsonElement>(didDocUrl, JsonOpts, ct);

        string? pds = null;
        if (doc.TryGetProperty("service", out var services))
        {
            foreach (var svc in services.EnumerateArray())
            {
                if (svc.TryGetProperty("id", out var id))
                {
                    var idStr = id.GetString() ?? "";
                    if (idStr == "#atproto_pds" || idStr.EndsWith("#atproto_pds", StringComparison.Ordinal))
                    {
                        if (svc.TryGetProperty("serviceEndpoint", out var ep))
                            pds = ep.GetString()?.TrimEnd('/');
                        break;
                    }
                }
            }
        }

        if (pds is null)
            throw new InvalidOperationException($"No #atproto_pds service found in DID document for {did}.");

        string? handle = null;
        if (doc.TryGetProperty("alsoKnownAs", out var aka))
        {
            foreach (var entry in aka.EnumerateArray())
            {
                var value = entry.GetString();
                if (value?.StartsWith("at://") == true)
                {
                    handle = value["at://".Length..];
                    break;
                }
            }
        }

        return new DidDocumentInfo(pds, handle);
    }

    /// <summary>
    /// Resolves a DID to its PDS endpoint. Use ResolveAsync when you also need the handle.
    /// </summary>
    public static async Task<string> ResolvePdsAsync(string did, HttpClient http, CancellationToken ct = default)
        => (await ResolveAsync(did, http, ct)).Pds;
}
