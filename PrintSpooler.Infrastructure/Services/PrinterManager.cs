using System.Collections.Concurrent;
using System.Threading.Channels;
using ErrorOr;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PrintSpooler.Core.Models;
using PrintSpooler.Core.Services;

namespace PrintSpooler.Infrastructure.Services;

public class PrinterManager(
    IServiceScopeFactory scopeFactory,
    IPrinterMonitorFactory printerMonitorFactory,
    Channel<IppJobRef> jobChannel,
    Channel<PrinterEvent> printerChannel,
    ILogger<PrinterManager> logger
    ) : BackgroundService
{
  private readonly ConcurrentDictionary<Guid, PrinterMonitor> _monitors = [];

  protected override async Task ExecuteAsync(CancellationToken ct)
  {
    var printers = await SeedPrinters();

    foreach (var printer in printers)
      AddPrinterMonitor(printer, ct);

    try
    {
      await Task.WhenAll(PrinterEvents(ct), RouteJobs(ct));
    }
    finally
    {
      await RetireAll();
    }
  }

  private void AddPrinterMonitor(Printer printer, CancellationToken ct) =>
    _monitors.TryAdd(printer.Id, printerMonitorFactory.Create(printer, ct));

  private async Task RetireAll()
  {
    foreach (var value in _monitors.Values)
      await value.DisposeAsync();

    _monitors.Clear();
  }

  private async Task<List<Printer>> SeedPrinters()
  {
    using var scope = scopeFactory.CreateAsyncScope();
    var printerService = scope.ServiceProvider.GetRequiredService<IPrinterService>();

    return await printerService.GetPrinters();
  }

  public async Task RouteJobs(CancellationToken ct)
  {
    await foreach (var job in jobChannel.Reader.ReadAllAsync(ct))
    {
      if (_monitors.TryGetValue(job.PrinterId, out var printerMonitor))
        printerMonitor.AddJob(job.IppId, job.JobId);
      else
      {
        logger.LogWarning("Job added to channel for printer without active monitor. Added monitor now");

        using var scope = scopeFactory.CreateAsyncScope();
        var printerService = scope.ServiceProvider.GetRequiredService<IPrinterService>();

        await printerService.GetPrinter(job.PrinterId)
          .ThenDo(p => AddPrinterMonitor(p, ct));

        await jobChannel.Writer.WriteAsync(job);
      }
    }
  }

  public async Task PrinterEvents(CancellationToken ct)
  {
    await foreach (var printerEvent in printerChannel.Reader.ReadAllAsync(ct))
    {
      using var scope = scopeFactory.CreateAsyncScope();
      var printerService = scope.ServiceProvider.GetRequiredService<IPrinterService>();

      switch (printerEvent.EventType)
      {
        case PrinterEventType.Add:
          await printerService.GetPrinter(printerEvent.PrinterId)
          .ThenDo(p => AddPrinterMonitor(p, ct));
          break;

        case PrinterEventType.Remove:
          if (_monitors.TryRemove(printerEvent.PrinterId, out var m))
            await m.DisposeAsync();
          break;
      }
    }
  }

}
