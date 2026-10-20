using PrintSpooler.Infrastructure.Data;

namespace PrintSpooler.Api.IntegrationTests;

public class SeededPrinterWebApplicationFactory : CustomWebApplicationFactory
{
  public Guid SeededPrinterId { get; private set; }

  protected override async Task SeedAsync(AppDbContext dbContext)
  {
    var printer = PrinterTestHelper.CreateUniquePrinter();
    printer.Id = Guid.NewGuid();

    dbContext.Add(printer);
    SeededPrinterId = printer.Id;

    await dbContext.SaveChangesAsync();
  }
}
