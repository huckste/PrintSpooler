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
    ILogger<PrinterHeartbeat> logger) : IDisposable
{
  private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);
  private static readonly TimeSpan PollTimeout = TimeSpan.FromSeconds(10);
  private readonly PeriodicTimer _timer = new(Interval);

  public void Dispose()
  {
    _timer.Dispose();
  }

  public async Task RunAsync(CancellationToken ct)
  {
    while (await _timer.WaitForNextTickAsync(ct))
    {
      // Without this an unresponsive printer stalls the loop indefinitely.
      using var pollCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
      pollCts.CancelAfter(PollTimeout);

      try
      {
        await Poll(pollCts.Token);
      }
      catch (OperationCanceledException) when (!ct.IsCancellationRequested)
      {
        HandleErrors([Error.Failure("PrinterHeartbeat.Timeout", $"No status from {printer.Name} within {PollTimeout.TotalSeconds}s")]);
      }
      catch (Exception ex) when (ex is not OperationCanceledException)
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
