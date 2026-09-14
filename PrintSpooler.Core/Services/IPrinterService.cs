namespace PrintSpooler.Core.Services;

using ErrorOr;
using PrintSpooler.Core.Models;

public interface IPrinterService
{
  Task<ErrorOr<Printer>> GetPrinter(Guid id);
  Task<ErrorOr<Printer>> CreatePrinter(Printer printer);
  Task<ErrorOr<Success>> UpdatePrinterStatus(Guid id, PrinterStatus status, string? reason = null, int? upTimeSeconds = null);
  Task<List<Printer>> GetPrinters();
  Task<ErrorOr<Success>> DeletePrinter(Guid id);
  Task UpdatePrinter(Printer printer, CancellationToken ct = default);
}
