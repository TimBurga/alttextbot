using AltTextBot.Application.Interfaces;
using AltTextBot.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AltTextBot.Web.Pages.Admin;

[Authorize]
public class SubscribersModel(IAdminService adminService) : PageModel
{
    public PagedResult<Subscriber> Subscribers { get; private set; } = new([], 0, 1, 20);

    public async Task OnGetAsync(int page = 1, CancellationToken ct = default)
    {
        Subscribers = await adminService.GetSubscribersAsync(page, 20, ct);
    }
}
