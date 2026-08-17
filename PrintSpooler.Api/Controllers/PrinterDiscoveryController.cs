namespace PrintSpooler.Api.Controllers;

using ErrorOr;
using Microsoft.AspNetCore.Mvc;
using PrintSpooler.Core.Services;

[ApiController]
[Route("[controller]")]
public class PrinterDiscoveryController(IPrinterDiscoveryService printerDiscoveryService) : ControllerBase
{
  [HttpGet(Name = "DiscoverPrinter")]
  public async Task<IActionResult> Get() => await printerDiscoveryService.ProbeForNetworkPrinters().Match(
       Ok,
      errors => Problem(detail: errors.First().Description, statusCode: 400)
      );

}
