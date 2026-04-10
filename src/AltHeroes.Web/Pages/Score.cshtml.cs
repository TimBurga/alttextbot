using AltHeroes.Web.Configuration;
using AltHeroes.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace AltHeroes.Web.Pages;

public sealed class ScoreModel(HandleResolver resolver, IOptions<ScoringOptions> scoring) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    public string? Did { get; private set; }
    public string? Error { get; private set; }
    public int WindowDays => scoring.Value.WindowDays;

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(Q))
            return RedirectToPage("/Index");

        Did = await resolver.ResolveAsync(Q.Trim(), ct);
        if (Did is null)
            Error = $"Could not find \"{Q}\". Check the handle or DID and try again.";

        return Page();
    }
}
