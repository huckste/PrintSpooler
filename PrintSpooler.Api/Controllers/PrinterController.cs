using ErrorOr;
using Microsoft.AspNetCore.Mvc;
using PrintSpooler.Api.Contracts;
using PrintSpooler.Core.Models;
using PrintSpooler.Core.Services;

namespace PrintSpooler.Api.Controllers;

[ApiController]
[Route("[controller]")]
public class PrinterController(IPrinterService printerService) : ControllerBase
{
    [HttpPost(Name = "PostPrinter")]
    public async Task<IActionResult> Post(CreatePrinterRequest printerRequest)
    {
        return await printerService
            .CreatePrinter(
                new Printer { Name = printerRequest.Name, IpAddress = printerRequest.IpAddress }
            )
            .Match(
                printer => CreatedAtAction(nameof(Post), new { id = printer.Id }, printer),
                errors => Problem(detail: errors.First().Description, statusCode: 400)
            );
    }

    [HttpGet("{id}", Name = "GetPrinter")]
    public async Task<IActionResult> Get(Guid id)
    {
        return await printerService
            .GetPrinter(id)
            .Match(Ok, errors => Problem(detail: errors.First().Description, statusCode: 400));
    }
}
