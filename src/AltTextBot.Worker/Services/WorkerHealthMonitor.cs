using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AltTextBot.Worker.Services;

/// <summary>
/// Tracks live connection state for background WebSocket workers so health checks
/// can report whether each worker is currently connected.
/// </summary>
public sealed class WorkerHealthMonitor
{
    private volatile bool _jetstreamConnected;
    private volatile bool _tapConnected;
    private DateTimeOffset _jetstreamConnectedSince = DateTimeOffset.MinValue;
    private DateTimeOffset _tapConnectedSince = DateTimeOffset.MinValue;

    public void SetJetstreamConnected(bool connected)
    {
        _jetstreamConnected = connected;
        if (connected) _jetstreamConnectedSince = DateTimeOffset.UtcNow;
    }

    public void SetTapConnected(bool connected)
    {
        _tapConnected = connected;
        if (connected) _tapConnectedSince = DateTimeOffset.UtcNow;
    }

    public bool IsJetstreamConnected => _jetstreamConnected;
    public bool IsTapConnected => _tapConnected;
    public DateTimeOffset JetstreamConnectedSince => _jetstreamConnectedSince;
    public DateTimeOffset TapConnectedSince => _tapConnectedSince;
}

public sealed class JetstreamHealthCheck(WorkerHealthMonitor monitor) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        if (monitor.IsJetstreamConnected)
        {
            var uptime = DateTimeOffset.UtcNow - monitor.JetstreamConnectedSince;
            return Task.FromResult(HealthCheckResult.Healthy($"Connected for {uptime:g}."));
        }

        return Task.FromResult(monitor.JetstreamConnectedSince == DateTimeOffset.MinValue
            ? HealthCheckResult.Unhealthy("Jetstream has never connected.")
            : HealthCheckResult.Degraded("Jetstream is disconnected (reconnecting)."));
    }
}

public sealed class TapHealthCheck(WorkerHealthMonitor monitor) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        if (monitor.IsTapConnected)
        {
            var uptime = DateTimeOffset.UtcNow - monitor.TapConnectedSince;
            return Task.FromResult(HealthCheckResult.Healthy($"Connected for {uptime:g}."));
        }

        return Task.FromResult(monitor.TapConnectedSince == DateTimeOffset.MinValue
            ? HealthCheckResult.Unhealthy("Tap has never connected.")
            : HealthCheckResult.Degraded("Tap is disconnected (reconnecting)."));
    }
}
