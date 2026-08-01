namespace PrintSpooler.Infrastructure.Services;

using ErrorOr;
using Microsoft.EntityFrameworkCore;
using PrintSpooler.Core.Models;
using PrintSpooler.Core.Services;
using PrintSpooler.Infrastructure.Data;

public class LogsService(AppDbContext dbContext) : ILogsService
{
    public async Task<ErrorOr<PagedResult<AuditLog>>> GetLogs(LogQueryParams queryParams)
    {
        IQueryable<AuditLog> query = dbContext.AuditLogs;

        if (!string.IsNullOrWhiteSpace(queryParams.SearchTerms))
            query = query.Where(l =>
                l.Details != null && l.Details.Contains(queryParams.SearchTerms)
            );

        if (queryParams.PerformedBy != null)
            query = query.Where(l => l.PerformedBy == queryParams.PerformedBy);

        if (queryParams.ActionFilter != null)
            query = query.Where(l => l.Action == queryParams.ActionFilter);

        if (queryParams.DateFrom != null)
            query = query.Where(l => DateOnly.FromDateTime(l.Timestamp) >= queryParams.DateFrom);

        if (queryParams.DateTo != null)
            query = query.Where(l => DateOnly.FromDateTime(l.Timestamp) <= queryParams.DateTo);

        try
        {
            var rowCount = await query.CountAsync();

            query = query
                .Skip((queryParams.Page - 1) * queryParams.PageSize)
                .Take(queryParams.PageSize);

            var results = await query.ToListAsync();

            return new PagedResult<AuditLog>
            {
                Items = results,
                TotalCount = rowCount,
                Page = queryParams.Page,
                PageSize = queryParams.PageSize,
            };
        }
        catch (Exception ex)
        {
            return Error.Unexpected("Logs.Unexpected", $"Error getting logs: {ex.Message}");
        }
    }
}
