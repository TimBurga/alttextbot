using AltTextBot.Domain.Enums;

namespace AltTextBot.Application.Interfaces;

public interface ILabelStateReader
{
    Task<LabelTier?> GetCurrentLabelAsync(string did, CancellationToken ct = default);
}
