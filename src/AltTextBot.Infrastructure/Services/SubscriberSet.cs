using System.Collections.Concurrent;
using AltTextBot.Application.Interfaces;

namespace AltTextBot.Infrastructure.Services;

public class SubscriberSet : ISubscriberSet
{
    private readonly ConcurrentDictionary<string, byte> _dids = new();
    private readonly TaskCompletionSource _initialized = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool Contains(string did) => _dids.ContainsKey(did);

    public void Add(string did) => _dids.TryAdd(did, 0);

    public void Remove(string did) => _dids.TryRemove(did, out _);

    public void Initialize(IEnumerable<string> dids)
    {
        _dids.Clear();
        foreach (var did in dids)
            _dids.TryAdd(did, 0);
    }

    public void MarkInitialized() => _initialized.TrySetResult();

    public Task WaitForInitializationAsync(CancellationToken ct = default)
        => _initialized.Task.WaitAsync(ct);
}
