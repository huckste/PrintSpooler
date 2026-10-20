using PrintSpooler.Api.Contracts;
using PrintSpooler.Core.Models;

namespace PrintSpooler.Api.IntegrationTests;

public static class PrinterTestHelper
{
  /// <summary>
  /// Creates a <see cref="Printer"/> whose <see cref="Printer.Name"/>, <see cref="Printer.IpAddress"/>,
  /// and <see cref="Printer.Host"/> are Guids, not real network values.
  /// </summary>
  public static Printer CreateUniquePrinter() =>
     new()
     {
       Name = $"Test_Printer_{Guid.NewGuid()}",
       IpAddress = Guid.NewGuid().ToString(),
       Host = Guid.NewGuid().ToString()
     };

  public static CreatePrinterRequest CreateUniquePrinterRequest()
  {
    var printer = CreateUniquePrinter();

    return new()
    {
      Name = printer.Name,
      IpAddress = printer.IpAddress,
      Host = printer.Host
    };
  }
}
