using Microsoft.AspNetCore.SignalR;
using PrintSpooler.Api.Hubs;
using PrintSpooler.Core.Models;
using PrintSpooler.Core.Services;

namespace PrintSpooler.Api.Services;

public class PrinterNotifier(IHubContext<UpdatesHub> hubContext) : IPrinterNotifier
{
  public async Task PrinterUpdateAsync(Printer printer, CancellationToken ct)
  {
    await hubContext.Clients.All.SendAsync("PrinterUpdated", printer, ct);
  }
}
