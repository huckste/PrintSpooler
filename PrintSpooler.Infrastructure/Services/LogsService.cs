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

    if (queryParams.JobId is { } jobId)
      query = query.Where(l => l.JobId == jobId);

    if (queryParams.PerformedBy is { } performedBy)
      query = query.Where(l => l.PerformedBy == performedBy);

    if (queryParams.ActionFilter is { } actionFilter)
      query = query.Where(l => l.Action == actionFilter);

    if (queryParams.DateFrom is { } dateFrom)
      query = query.Where(l => DateOnly.FromDateTime(l.Timestamp) >= dateFrom);

    if (queryParams.DateTo is { } dateTo)
      query = query.Where(l => DateOnly.FromDateTime(l.Timestamp) <= dateTo);

    query = queryParams.OrderByField switch
    {
      OrderByField.JobAction => queryParams.SortDirection == SortDirection.Desc
          ? query.OrderByDescending(l => l.Action)
          : query.OrderBy(l => l.Action),
      OrderByField.ByWho => queryParams.SortDirection == SortDirection.Desc
          ? query.OrderByDescending(l => l.PerformedBy)
          : query.OrderBy(l => l.PerformedBy),
      OrderByField.Details => queryParams.SortDirection == SortDirection.Desc
          ? query.OrderByDescending(l => l.Details)
          : query.OrderBy(l => l.Details),
      _ => queryParams.SortDirection == SortDirection.Desc
          ? query.OrderByDescending(l => l.Timestamp)
          : query.OrderBy(l => l.Timestamp),
    };

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
