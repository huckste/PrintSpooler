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
            p.IpAddress == printer.IpAddress || p.Name == printer.Name
        );

        if (isDuplicate)
            return Error.Conflict(
                "Printer.Conflict",
                $"Printer already exist with name {printer.Name} or ipAddress {printer.IpAddress} "
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
}
