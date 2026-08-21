namespace PrintSpooler.Infrastructure.Services;

using Microsoft.Extensions.DependencyInjection;
using PrintSpooler.Core.Models;
using PrintSpooler.Core.Services;
using ErrorOr;
using Microsoft.Extensions.Logging;

public class PrinterHeartbeat(
    Printer printer,
    IServiceScopeFactory scopeFactory,
    IPrinterDispatcher printerDispatcher,
    ILogger<PrinterPoller> logger) : IDisposable
{
  private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);
  private readonly PeriodicTimer _timer = new(Interval);
  private bool _disposed;

  public void Dispose()
  {
    _timer.Dispose();
    _disposed = true;
  }

  public async Task RunAsync(CancellationToken ct)
  {
    ObjectDisposedException.ThrowIf(_disposed, this);

    while (await _timer.WaitForNextTickAsync(ct))
    {
      try
      {
        await Poll(ct);
      }
      catch (Exception ex)
      {
        HandleErrors([Error.Unexpected("PrinterHeartbeat.RunAsync", $"Ex: {ex.Message}")]);
      }
    }
  }

  private async Task Poll(CancellationToken ct)
  {
    var result = await printerDispatcher.GetPrinterStatusAsync(printer, ct)
      .ThenAsync(async status => await HandleStatusUpdate(status, ct));

    if (result.IsError)
      HandleErrors(result.Errors);
  }

  private void HandleErrors(List<Error> errors)
  {
    foreach (var e in errors)
      logger.LogError("{Printer}: {Code} - {Description}", printer.Name, e.Code, e.Description);
  }

  private async Task<ErrorOr<Success>> HandleStatusUpdate(PrinterStatus status, CancellationToken ct)
  {
    using var scope = scopeFactory.CreateScope();
    var printerService = scope.ServiceProvider.GetRequiredService<IPrinterService>();

    return await printerService.GetPrinter(printer.Id)
      .ThenDoAsync(async p =>
      {
        p.Status = status;
        p.LastHeartbeat = DateTime.UtcNow;
        await printerService.UpdatePrinter(p, ct);
      })
      .Then(_ => Result.Success);
  }
}
