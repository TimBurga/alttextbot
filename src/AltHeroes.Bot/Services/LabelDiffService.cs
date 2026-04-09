using AltHeroes.Bot;
using AltHeroes.Core;

namespace AltHeroes.Bot.Services;

/// <summary>
/// Applies a tier change to Ozone and optionally posts congrats.
/// Centralises the diff logic used by both startup backfill and lazy rescore.
/// </summary>
public sealed class LabelDiffService(
    OzoneClient ozone,
    CongratsPostService congrats,
    BotState state,
    ILogger<LabelDiffService> logger)
{
    /// <summary>
    /// If <paramref name="newTier"/> differs from the current tier in <see cref="BotState"/>,
    /// updates Ozone labels and posts congrats on upgrade.
    /// </summary>
    public async Task ApplyIfChangedAsync(
        string did,
        string handle,
        LabelTier newTier,
        CancellationToken ct = default)
    {
        var oldTier = state.GetCurrentTier(did);
        if (newTier == oldTier) return;

        logger.LogInformation(
            "LabelDiffService: {Did} tier {Old} → {New}",
            did, oldTier, newTier);

        await ozone.UpdateLabelAsync(did, newTier, oldTier, ct);
        state.SetCurrentTier(did, newTier);

        if (newTier > oldTier)
            await congrats.PostCongratsAsync(did, handle, newTier, ct);
    }
}
