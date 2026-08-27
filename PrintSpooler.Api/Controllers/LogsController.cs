using ErrorOr;
using Microsoft.AspNetCore.Mvc;
using PrintSpooler.Core.Models;
using PrintSpooler.Core.Services;

namespace PrintSpooler.Api.Controllers;

/// <summary>
/// Read-only queries over the audit log.
/// Single paged endpoint rather than per-row routes.
/// </summary>

[ApiController]
[Route("[controller]")]
public class LogsController(ILogsService logService) : ControllerBase
{
  /// <summary>
  /// GET /Logs — filter, sort, page audit entries via <see cref="LogQueryParams"/>.
  /// <see cref="LogQueryParams"/> is a complex type, so it requires [FromQuery]
  /// </summary>

  [HttpGet(Name = "GetLogs")]
  public async Task<IActionResult> Get([FromQuery] LogQueryParams queryParams) =>
      await logService
          .GetLogs(queryParams)
          .Match(Ok, error => Problem(detail: error.First().Description, statusCode: 400));
}
