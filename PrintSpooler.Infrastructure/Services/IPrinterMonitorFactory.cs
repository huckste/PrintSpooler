using PrintSpooler.Core.Models;

namespace PrintSpooler.Infrastructure.Services;

public interface IPrinterMonitorFactory
{
  PrinterMonitor Create(Printer printer, CancellationToken ct);
}
