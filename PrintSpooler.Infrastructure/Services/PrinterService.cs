namespace PrintSpooler.Infrastructure.Services;

using ErrorOr;
using Microsoft.EntityFrameworkCore;
using PrintSpooler.Core.Models;
using PrintSpooler.Core.Services;
using PrintSpooler.Infrastructure.Data;

public class PrinterService(AppDbContext dbContext) : IPrinterService
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

  public async Task<List<Printer>> GetPrinters() => await dbContext.Printers.ToListAsync();

  public async Task<ErrorOr<Success>> DeletePrinter(Guid id)
  {
    Printer? printer = await dbContext.Printers.FirstOrDefaultAsync(p => p.Id == id);

    if (printer is null)
      return Error.NotFound("Printer.NotFound", $"No printer foudn with ID {id}");

    dbContext.Printers.Remove(printer);
    await dbContext.SaveChangesAsync();

    return Result.Success;
  }
}
