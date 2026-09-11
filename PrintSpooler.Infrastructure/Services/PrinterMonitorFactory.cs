using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PrintSpooler.Core.Models;
using PrintSpooler.Core.Services;

namespace PrintSpooler.Infrastructure.Services;

public sealed class PrinterMonitorFactory(
    IServiceScopeFactory scopeFactory,
    IPrinterDispatcher printerDispatcher,
    ILogger<PrinterMonitor> logger
    ) : IPrinterMonitorFactory
{
  public PrinterMonitor Create(Printer printer, CancellationToken hostToken) =>
    new(printer, hostToken, scopeFactory, printerDispatcher, logger);
}
