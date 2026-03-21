using AltTextBot.Application.Interfaces;
using AltTextBot.Domain.Enums;
using AltTextBot.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AltTextBot.Web.Pages.User;

public class LookupModel(
    AltTextBotDbContext db,
    IScoringService scoringService,
    ILabelStateReader labelStateReader) : PageModel
{
    public string? Handle { get; private set; }
    public ScoringWindow? Score { get; private set; }
    public LabelTier? CurrentTier { get; private set; }
    public DateTimeOffset? LastScoredAt { get; private set; }

    public async Task<IActionResult> OnGetAsync(string? handle, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(handle))
            return RedirectToPage("/Index");

        Handle = handle.TrimStart('@');

        var subscriber = await db.Subscribers
            .FirstOrDefaultAsync(s => s.Handle == Handle, ct);

        if (subscriber is null)
            return Page();

        Score = await scoringService.ComputeScoreAsync(subscriber.Did, ct);
        CurrentTier = await labelStateReader.GetCurrentLabelAsync(subscriber.Did, ct);
        LastScoredAt = subscriber.LastScoredAt;

        return Page();
    }
}
