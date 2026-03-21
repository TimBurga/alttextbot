namespace AltTextBot.Application.Interfaces;

public interface ISubscriberService
{
    Task SubscribeAsync(string did, string handle, CancellationToken ct = default);
    Task UnsubscribeAsync(string did, CancellationToken ct = default);
    Task ReactivateAsync(string did, CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetActiveSubscriberDidsAsync(CancellationToken ct = default);
}
