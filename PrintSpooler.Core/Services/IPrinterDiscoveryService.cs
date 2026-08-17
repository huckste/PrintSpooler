namespace PrintSpooler.Core.Services;

using ErrorOr;
using PrintSpooler.Core.Models;

public interface IPrinterDiscoveryService
{
  public Task<ErrorOr<List<Printer>>> ProbeForNetworkPrinters();
}
