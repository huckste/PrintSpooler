using System.Collections.Concurrent;
using PrintSpooler.Core.Models;
using PrintSpooler.Infrastructure.Services;

namespace PrintSpooler.Api.IntegrationTests;

/// <summary>
/// Test wrapper around the real <c>PrinterMonitorFactory</c>.
/// Records every monitor <c>PrinterManager</c> creates, keyed by printer id.
/// </summary>
public class RecordingPrinterMonitorFactory(PrinterMonitorFactory printerMonitorFactory) : IPrinterMonitorFactory
{
  public ConcurrentDictionary<Guid, PrinterMonitor> Monitors { get; } = [];

  public PrinterMonitor Create(Printer printer, CancellationToken ct)
  {
    var printerMonitor = printerMonitorFactory.Create(printer, ct);
    Monitors.TryAdd(printer.Id, printerMonitor);

    return printerMonitor;
  }
}
