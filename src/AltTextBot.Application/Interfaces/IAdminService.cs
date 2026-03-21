using AltTextBot.Domain.Entities;

namespace AltTextBot.Application.Interfaces;

public record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize);

public interface IAdminService
{
    Task<PagedResult<Subscriber>> GetSubscribersAsync(int page, int pageSize, CancellationToken ct = default);
    Task<Subscriber?> GetSubscriberAsync(string did, CancellationToken ct = default);
    Task ManualRescoreAsync(string did, CancellationToken ct = default);
    Task<IReadOnlyList<AuditLog>> GetRecentAuditLogsAsync(string did, int count, CancellationToken ct = default);
    Task<(IReadOnlyList<AuditLog> Logs, bool HasMore)> GetAuditLogsPageAsync(int page, int pageSize, CancellationToken ct = default);
}
