using AltTextBot.Application.Interfaces;
using AltTextBot.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AltTextBot.Web.Pages.Admin;

[Authorize]
public class AuditModel(IAdminService adminService) : PageModel
{
    public IReadOnlyList<AuditLog> Logs { get; private set; } = [];
    public new int Page { get; private set; }
    public bool HasMore { get; private set; }

    public async Task OnGetAsync(int page = 1, CancellationToken ct = default)
    {
        Page = page;
        (Logs, HasMore) = await adminService.GetAuditLogsPageAsync(page, 100, ct);
    }
}
