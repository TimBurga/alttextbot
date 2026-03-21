namespace AltTextBot.Application.Interfaces;

/// <summary>
/// In-memory set of active subscriber DIDs for fast Jetstream filtering.
/// Populated by StartupSyncService; updated by JetstreamWorker.
/// </summary>
public interface ISubscriberSet
{
    bool Contains(string did);
    void Add(string did);
    void Remove(string did);
    Task WaitForInitializationAsync(CancellationToken ct = default);
    void MarkInitialized();
    void Initialize(IEnumerable<string> dids);
}
