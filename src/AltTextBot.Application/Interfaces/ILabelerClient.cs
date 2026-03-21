using AltTextBot.Domain.Enums;

namespace AltTextBot.Application.Interfaces;

public interface ILabelerClient
{
    Task ApplyLabelAsync(string did, LabelTier tier, CancellationToken ct = default);
    Task NegateLabelAsync(string did, LabelTier tier, CancellationToken ct = default);
}
