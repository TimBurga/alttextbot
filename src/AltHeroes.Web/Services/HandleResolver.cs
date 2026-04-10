using System.Net.Http.Json;
using System.Text.Json;

namespace AltHeroes.Web.Services;

public sealed class HandleResolver(IHttpClientFactory httpClientFactory, ILogger<HandleResolver> logger)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Returns the DID for a handle, or the DID itself if already a DID.
    /// Returns null if resolution fails.
    /// </summary>
    public async Task<string?> ResolveAsync(string handleOrDid, CancellationToken ct = default)
    {
        if (handleOrDid.StartsWith("did:", StringComparison.OrdinalIgnoreCase))
            return handleOrDid;

        var handle = handleOrDid.TrimStart('@');

        try
        {
            var url = "https://public.api.bsky.app/xrpc/com.atproto.identity.resolveHandle" +
                      $"?handle={Uri.EscapeDataString(handle)}";

            var http = httpClientFactory.CreateClient(nameof(HandleResolver));
            var response = await http.GetFromJsonAsync<ResolveHandleResponse>(url, JsonOpts, ct);
            return response?.Did;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "HandleResolver: Failed to resolve {Handle}.", handle);
            return null;
        }
    }

    private record ResolveHandleResponse(string Did);
}
