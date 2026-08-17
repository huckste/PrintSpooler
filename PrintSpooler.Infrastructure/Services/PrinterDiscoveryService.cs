namespace PrintSpooler.Infrastructure.Services;

using Zeroconf;
using PrintSpooler.Core.Models;
using ErrorOr;
using PrintSpooler.Core.Services;

public class PrinterDiscoveryService : IPrinterDiscoveryService
{
  public async Task<ErrorOr<List<Printer>>> ProbeForNetworkPrinters()
  {

    try
    {
      List<Printer> printers = [];
      var results = await ZeroconfResolver.ResolveAsync("_ipp._tcp.local.");

      foreach (var host in results)
      {
        var service = host.Services.Values.First();
        var txt = service.Properties.First();

        printers.Add(new Printer
        {
          Name = txt.GetValueOrDefault("ty") ?? host.DisplayName,
          IpAddress = host.IPAddress,
          Host = new Uri(txt["adminurl"]).Host.TrimEnd('.'),
          PrinterUuid = txt.GetValueOrDefault("UUID"),
          SupportedContentTypes = txt.GetValueOrDefault("pdl")?.Split(",").ToList()
        });
      }

      return printers;
    }
    catch (Exception ex)
    {
      return Error.Unexpected("ProbeForNetworkPrinters.Unexpected", ex.Message);
    }

  }
}
