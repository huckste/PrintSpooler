using ErrorOr;
using PrintSpooler.Core.Models;

namespace PrintSpooler.Web.Services;

public class PrinterApi(ApiClient api)
{
  public Task<ErrorOr<List<Printer>>> GetPrinters() =>
     api.Get<Printer>("/Printer");

  public Task<ErrorOr<List<Printer>>> DiscoverNetworkPrinters() =>
     api.Get<Printer>("/PrinterDiscovery");

  public Task<ErrorOr<Printer>> AddPrinter(Printer printer) =>
     api.Post<Printer>("/Printer", printer);

  public Task<ErrorOr<Success>> DeletePrinter(Guid id) =>
      api.Delete($"/Printer/{id}");

}
