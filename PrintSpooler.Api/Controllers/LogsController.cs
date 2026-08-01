using ErrorOr;
using Microsoft.AspNetCore.Mvc;
using PrintSpooler.Core.Models;
using PrintSpooler.Core.Services;

namespace PrintSpooler.Api.Controllers;

[ApiController]
[Route("[controller]")]
public class LogsController(ILogsService logService) : ControllerBase
{
    [HttpGet(Name = "GetLogs")]
    public async Task<IActionResult> Get([FromQuery] LogQueryParams queryParams) =>
        await logService
            .GetLogs(queryParams)
            .Match(Ok, error => Problem(detail: error.First().Description, statusCode: 400));
}
