using System.Text.Json;

namespace AltHeroes.Bot;

/// <summary>
/// Persists a set of blocked DIDs to a JSON file so blocks survive restarts.
/// Blocked subscribers stay enrolled (so unenroll-via-unlike still works)
/// but are skipped for scoring and labeling.
/// </summary>
public sealed class BlockedSubscribersStore
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _fileLock = new(1, 1);
    private readonly ILogger<BlockedSubscribersStore> _logger;
    private HashSet<string> _blocked = [];

    public BlockedSubscribersStore(IConfiguration configuration, ILogger<BlockedSubscribersStore> logger)
    {
        _logger = logger;
        var dataDir = configuration["DataDir"] ?? "data";
        _filePath = Path.Combine(dataDir, "blocked.json");
    }

    public async Task LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_filePath))
        {
            _blocked = [];
            return;
        }

        try
        {
            await using var stream = File.OpenRead(_filePath);
            var dids = await JsonSerializer.DeserializeAsync<List<string>>(stream, cancellationToken: ct);
            _blocked = dids is not null ? [.. dids] : [];
            _logger.LogInformation("BlockedSubscribersStore: Loaded {Count} blocked DIDs.", _blocked.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "BlockedSubscribersStore: Could not load {Path}, starting empty.", _filePath);
            _blocked = [];
        }
    }

    public bool IsBlocked(string did) => _blocked.Contains(did);

    public async Task BlockAsync(string did, CancellationToken ct = default)
    {
        _blocked.Add(did);
        await PersistAsync(ct);
        _logger.LogInformation("BlockedSubscribersStore: Blocked {Did}.", did);
    }

    public async Task UnblockAsync(string did, CancellationToken ct = default)
    {
        _blocked.Remove(did);
        await PersistAsync(ct);
        _logger.LogInformation("BlockedSubscribersStore: Unblocked {Did}.", did);
    }

    public IReadOnlySet<string> All() => _blocked;

    private async Task PersistAsync(CancellationToken ct)
    {
        await _fileLock.WaitAsync(ct);
        try
        {
            var dir = Path.GetDirectoryName(_filePath)!;
            Directory.CreateDirectory(dir);
            var tmp = _filePath + ".tmp";
            await File.WriteAllTextAsync(tmp, JsonSerializer.Serialize(_blocked.ToList()), ct);
            File.Move(tmp, _filePath, overwrite: true);
        }
        finally { _fileLock.Release(); }
    }
}
