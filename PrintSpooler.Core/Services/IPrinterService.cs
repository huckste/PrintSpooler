namespace PrintSpooler.Core.Services;

using ErrorOr;
using PrintSpooler.Core.Models;

public interface IPrinterService
{
    Task<ErrorOr<Printer>> GetPrinter(Guid id);
    Task<ErrorOr<Printer>> CreatePrinter(Printer printer);
}
