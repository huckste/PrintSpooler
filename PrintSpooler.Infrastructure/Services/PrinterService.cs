namespace PrintSpooler.Infrastructure.Services;

using ErrorOr;
using Microsoft.EntityFrameworkCore;
using PrintSpooler.Core.Models;
using PrintSpooler.Core.Services;
using PrintSpooler.Infrastructure.Data;

public class PrinterService(AppDbContext dbContext, IPrinterNotifier printerNotifier) : IPrinterService
{
  public async Task<ErrorOr<Printer>> CreatePrinter(Printer printer)
  {
    bool isDuplicate = await dbContext.Printers.AnyAsync(p =>
        p.IpAddress == printer.IpAddress || p.Name == printer.Name || p.Host == printer.Host
    );

    if (isDuplicate)
      return Error.Conflict(
          "Printer.Conflict",
          $"Printer already exist with name {printer.Name} or ipAddress {printer.IpAddress} or host {printer.Host}"
      );

    printer.Id = Guid.NewGuid();

    dbContext.Printers.Add(printer);
    await dbContext.SaveChangesAsync();

    return printer;
  }

  public async Task<ErrorOr<Printer>> GetPrinter(Guid id)
  {
    var printer = await dbContext.Printers.FirstOrDefaultAsync(j => j.Id == id);

    if (printer is null)
      return Error.NotFound("Printer.NotFound", $"No printer found with ID {id}");

    return printer;
  }

  public async Task<ErrorOr<Success>> UpdatePrinterStatus(Guid id, PrinterStatus status)
  {
    var printer = await GetPrinter(id);

    if (printer.IsError)
      return printer.Errors;

    printer.Value.Status = status;

    await UpdatePrinter(printer.Value);

    return Result.Success;
  }

  public async Task<List<Printer>> GetPrinters() => await dbContext.Printers.ToListAsync();

  public async Task<ErrorOr<Success>> DeletePrinter(Guid id) =>
    await GetPrinter(id)
      .FailIfAsync(
        p => dbContext.Jobs.AnyAsync(j => j.PrinterId == p.Id && JobPolicies.Active.Contains(j.Status)),
        async p => Error.Conflict("Printer.HasActiveJobs", $"Cannot delete {p.Name}: has active jobs")
      )
      .ThenDo(p => dbContext.Printers.Remove(p))
      .ThenDoAsync(async p => await UpdatePrinter(p))
      .Then(_ => Result.Success);

  public async Task UpdatePrinter(Printer printer, CancellationToken ct = default)
  {
    await dbContext.SaveChangesAsync(ct);
    await printerNotifier.PrinterUpdateAsync(printer, ct);
  }
}
