using ErrorOr;
using PrintSpooler.Core.Models;

namespace PrintSpooler.Core.Services;

public interface ILogsService
{
    Task<ErrorOr<PagedResult<AuditLog>>> GetLogs(LogQueryParams query);
}
