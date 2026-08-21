namespace PrintSpooler.Infrastructure.Services;

using Microsoft.Extensions.DependencyInjection;
using PrintSpooler.Core.Models;
using PrintSpooler.Core.Services;
using ErrorOr;

public class PrinterHeartbeat(Printer printer, IServiceScopeFactory scopeFactory, IPrinterDispatcher printerDispatcher)
{
  private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);
  private readonly PeriodicTimer _timer = new(Interval);

  public async Task RunAsync(CancellationToken ct)
  {
    while (await _timer.WaitForNextTickAsync(ct))
    {
      try
      {
        await Poll(ct);
      }
      catch (Exception ex)
      {
        Console.WriteLine($"PrinterHeartbeat.Exception: {ex.Message}");
      }
    }
  }

  private async Task Poll(CancellationToken ct)
  {
    await printerDispatcher.GetPrinterStatusAsync(printer, ct).Switch(
        async status => await HandleStatusUpdate(status, ct),
        HandleErrors
       );
  }

  private void HandleErrors(List<Error> errors)
  {
    foreach (var error in errors)
      Console.WriteLine($"PrinterHeartbeat.Error: {error.Description}");
  }

  private async Task HandleStatusUpdate(PrinterStatus status, CancellationToken ct)
  {
    using var scope = scopeFactory.CreateScope();
    var printerService = scope.ServiceProvider.GetRequiredService<IPrinterService>();

    await printerService.GetPrinter(printer.Id)
      .ThenDo(p =>
      {
        p.Status = status;
        p.LastHeartbeat = DateTime.UtcNow;
      })
      .ThenDoAsync(async p => await printerService.UpdatePrinter(p, ct));
  }
}
