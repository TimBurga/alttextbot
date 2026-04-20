using System.Net.Http.Json;
using System.Text.Json;

namespace AltHeroes.Core;

public static class DidResolver
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Resolves a DID to its PDS endpoint by fetching the DID document.
    /// did:plc → plc.directory; did:web → /.well-known/did.json on the domain.
    /// Throws <see cref="InvalidOperationException"/> if no #atproto_pds service is found.
    /// </summary>
    public static async Task<string> ResolvePdsAsync(string did, HttpClient http, CancellationToken ct = default)
    {
        var didDocUrl = did.StartsWith("did:web:")
            ? $"https://{did["did:web:".Length..]}/.well-known/did.json"
            : $"https://plc.directory/{Uri.EscapeDataString(did)}";

        var doc = await http.GetFromJsonAsync<JsonElement>(didDocUrl, JsonOpts, ct);

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
                            return ep.GetString()?.TrimEnd('/')
                                   ?? throw new InvalidOperationException("Empty PDS endpoint.");
                    }
                }
            }
        }

        throw new InvalidOperationException($"No #atproto_pds service found in DID document for {did}.");
    }

    /// <summary>
    /// Resolves a DID to its handle by reading the alsoKnownAs field of the DID document.
    /// Returns null if the handle cannot be determined.
    /// </summary>
    public static async Task<string?> ResolveHandleAsync(string did, HttpClient http, CancellationToken ct = default)
    {
        var didDocUrl = did.StartsWith("did:web:")
            ? $"https://{did["did:web:".Length..]}/.well-known/did.json"
            : $"https://plc.directory/{Uri.EscapeDataString(did)}";

        var doc = await http.GetFromJsonAsync<JsonElement>(didDocUrl, JsonOpts, ct);

        if (doc.TryGetProperty("alsoKnownAs", out var aka))
        {
            foreach (var entry in aka.EnumerateArray())
            {
                var value = entry.GetString();
                if (value?.StartsWith("at://") == true)
                    return value["at://".Length..];
            }
        }

        return null;
    }
}
