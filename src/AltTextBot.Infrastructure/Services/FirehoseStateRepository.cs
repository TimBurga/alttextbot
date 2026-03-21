using AltTextBot.Application.Interfaces;
using AltTextBot.Domain.Entities;
using AltTextBot.Infrastructure.Data;

namespace AltTextBot.Infrastructure.Services;

public class FirehoseStateRepository(AltTextBotDbContext db) : IFirehoseStateRepository
{
    public async Task<long> GetCursorAsync(CancellationToken ct = default)
    {
        var state = await db.FirehoseStates.FindAsync([1], ct);
        return state?.LastTimeUs ?? 0;
    }

    public async Task SaveCursorAsync(long timeUs, CancellationToken ct = default)
    {
        var state = await db.FirehoseStates.FindAsync([1], ct);
        if (state is null)
        {
            state = FirehoseState.Create();
            db.FirehoseStates.Add(state);
        }
        state.UpdateCursor(timeUs);
        await db.SaveChangesAsync(ct);
    }
}
