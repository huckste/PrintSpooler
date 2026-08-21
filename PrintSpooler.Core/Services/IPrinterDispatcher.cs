using ErrorOr;
using PrintSpooler.Core.Models;

namespace PrintSpooler.Core.Services;

public interface IPrinterDispatcher
{
  Task<ErrorOr<IppJobRef>> SendAsync(Job job, byte[]? jobData, CancellationToken ct);
  Task<ErrorOr<List<IppJobStatus>>> GetPrinterJobsAsync(Printer printer, int[] ids, CancellationToken ct);
  Task<ErrorOr<PrinterStatus>> GetPrinterStatusAsync(Printer printer, CancellationToken ct);
}
