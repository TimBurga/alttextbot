namespace AltHeroes.Bot;

/// <summary>
/// Coordinates startup: BotStartupService signals completion so that
/// JetstreamWorker waits before processing events from T0.
/// </summary>
public sealed class StartupGate
{
    private readonly TaskCompletionSource _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void MarkComplete() => _tcs.TrySetResult();

    public Task WaitAsync(CancellationToken ct) =>
        _tcs.Task.WaitAsync(ct);
}
