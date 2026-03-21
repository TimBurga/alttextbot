using AltTextBot.Application.Interfaces;
using AltTextBot.Domain.Entities;
using AltTextBot.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AltTextBot.Web.Pages.Admin;

[Authorize]
public class SubscriberDetailModel(
    IAdminService adminService,
    IScoringService scoringService,
    ILabelStateReader labelStateReader,
    ISubscriberService subscriberService) : PageModel
{
    public Subscriber? Subscriber { get; private set; }
    public ScoringWindow? Score { get; private set; }
    public LabelTier? CurrentTier { get; private set; }
    public IReadOnlyList<AuditLog> AuditLogs { get; private set; } = [];

    public async Task OnGetAsync(string did, CancellationToken ct)
    {
        Subscriber = await adminService.GetSubscriberAsync(did, ct);
        if (Subscriber is null) return;

        Score = await scoringService.ComputeScoreAsync(did, ct);
        CurrentTier = await labelStateReader.GetCurrentLabelAsync(did, ct);
        AuditLogs = await adminService.GetRecentAuditLogsAsync(did, 50, ct);
    }

    public async Task<IActionResult> OnPostRescoreAsync(string did, CancellationToken ct)
    {
        await adminService.ManualRescoreAsync(did, ct);
        TempData["Success"] = "Rescore triggered. Score will update shortly.";
        return RedirectToPage(new { did });
    }

    public async Task<IActionResult> OnPostDeactivateAsync(string did, CancellationToken ct)
    {
        await subscriberService.UnsubscribeAsync(did, ct);
        TempData["Success"] = "Subscriber deactivated.";
        return RedirectToPage(new { did });
    }

    public async Task<IActionResult> OnPostReactivateAsync(string did, CancellationToken ct)
    {
        await subscriberService.ReactivateAsync(did, ct);
        TempData["Success"] = "Subscriber reactivated.";
        return RedirectToPage(new { did });
    }
}
