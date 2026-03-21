using AltTextBot.Domain.Enums;

namespace AltTextBot.Application.Interfaces;

public interface IBlueskyPostClient
{
    Task PostCongratsAsync(string did, string? handle, LabelTier newTier, CancellationToken ct = default);
}
