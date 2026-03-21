using AltTextBot.Domain.Enums;
using AltTextBot.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AltTextBot.Web.Pages.Admin;

[Authorize]
public class AdminIndexModel(AltTextBotDbContext db) : PageModel
{
    public int TotalSubscribers { get; private set; }
    public int ActiveSubscribers { get; private set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        TotalSubscribers = await db.Subscribers.CountAsync(ct);
        ActiveSubscribers = await db.Subscribers.CountAsync(s => s.Status == SubscriberStatus.Active, ct);
    }
}
