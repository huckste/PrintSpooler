using PrintSpooler.Core.Models;

namespace PrintSpooler.Core.Services;

public interface IPrinterDispatcher
{
    Task<bool> SendAsync(Job job, Printer printer, CancellationToken ct);
}
