using PrintSpooler.Core.Models;

namespace PrintSpooler.Core.Services;

public interface IPrinterNotifier
{
  Task PrinterUpdateAsync(Printer printer, CancellationToken ct);
}
