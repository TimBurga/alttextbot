using AltHeroes.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AltHeroes.Web.Pages.Admin;

[Authorize]
public class IndexModel(BotAdminClient bot) : PageModel
{
    public SubscribersResponse? Subscribers { get; private set; }
    public string? Error { get; private set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        try
        {
            Subscribers = await bot.GetSubscribersAsync(ct);
        }
        catch (Exception ex)
        {
            Error = $"Could not reach the Bot service: {ex.Message}";
        }
    }
}
