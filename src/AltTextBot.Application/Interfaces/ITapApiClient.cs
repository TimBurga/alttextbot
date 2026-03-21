namespace AltTextBot.Application.Interfaces;

public interface ITapApiClient
{
    Task AddReposAsync(IEnumerable<string> dids, CancellationToken ct = default);
    Task RemoveReposAsync(IEnumerable<string> dids, CancellationToken ct = default);
}
