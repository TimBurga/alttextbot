using AltHeroes.Core;

namespace AltHeroes.Bot;

/// <summary>
/// Thread-safe in-memory subscriber state.
/// _likeRkeyIndex maps the rkey of a like record (in the subscriber's repo)
/// to that subscriber's DID, enabling delete-event handling without an API round-trip.
/// </summary>
public sealed class BotState : IDisposable
{
    private readonly ReaderWriterLockSlim _lock = new();
    private readonly HashSet<string> _subscriberDids = [];
    private readonly Dictionary<string, string> _likeRkeyIndex = []; // rkey → DID
    private readonly Dictionary<string, LabelTier> _currentTiers = [];

    public void Enroll(string did, string rkey)
    {
        _lock.EnterWriteLock();
        try
        {
            _subscriberDids.Add(did);
            if (!string.IsNullOrWhiteSpace(rkey))
                _likeRkeyIndex[rkey] = did;
        }
        finally { _lock.ExitWriteLock(); }
    }

    /// <summary>Returns the DID whose like was deleted, or null if the rkey is unknown.</summary>
    public string? Unenroll(string rkey)
    {
        _lock.EnterWriteLock();
        try
        {
            if (!_likeRkeyIndex.TryGetValue(rkey, out var did)) return null;
            _likeRkeyIndex.Remove(rkey);
            _subscriberDids.Remove(did);
            _currentTiers.Remove(did);
            return did;
        }
        finally { _lock.ExitWriteLock(); }
    }

    public bool Contains(string did)
    {
        _lock.EnterReadLock();
        try { return _subscriberDids.Contains(did); }
        finally { _lock.ExitReadLock(); }
    }

    public IReadOnlyList<string> AllSubscriberDids()
    {
        _lock.EnterReadLock();
        try { return [.. _subscriberDids]; }
        finally { _lock.ExitReadLock(); }
    }

    public LabelTier GetCurrentTier(string did)
    {
        _lock.EnterReadLock();
        try { return _currentTiers.GetValueOrDefault(did, LabelTier.None); }
        finally { _lock.ExitReadLock(); }
    }

    public void SetCurrentTier(string did, LabelTier tier)
    {
        _lock.EnterWriteLock();
        try { _currentTiers[did] = tier; }
        finally { _lock.ExitWriteLock(); }
    }

    public int SubscriberCount
    {
        get
        {
            _lock.EnterReadLock();
            try { return _subscriberDids.Count; }
            finally { _lock.ExitReadLock(); }
        }
    }

    public void Dispose() => _lock.Dispose();
}
