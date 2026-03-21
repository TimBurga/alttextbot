namespace AltTextBot.Application.Interfaces;

public interface IFirehoseStateRepository
{
    Task<long> GetCursorAsync(CancellationToken ct = default);
    Task SaveCursorAsync(long timeUs, CancellationToken ct = default);
}
