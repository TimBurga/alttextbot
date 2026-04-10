using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AltHeroes.Web.Pages;

public sealed class IndexModel : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    public IActionResult OnGet()
    {
        // If a query was provided via direct GET /index?q=..., forward to /score
        if (!string.IsNullOrWhiteSpace(Q))
            return RedirectToPage("/Score", new { q = Q });

        return Page();
    }
}
