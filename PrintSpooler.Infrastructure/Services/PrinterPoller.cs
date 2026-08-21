using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PrintSpooler.Core.Models;
using PrintSpooler.Core.Services;

namespace PrintSpooler.Infrastructure.Services;

public class PrinterPoller(IServiceScopeFactory scopeFactory, Channel<IppJobRef> ippJobChannel, IPrinterDispatcher printerDispatcher) : BackgroundService
{
  protected override async Task ExecuteAsync(CancellationToken ct)
  {
    List<Printer> printers;

    using (var scope = scopeFactory.CreateScope())
    {
      var printerService = scope.ServiceProvider.GetRequiredService<IPrinterService>();
      printers = await printerService.GetPrinters();
    }

    var watches = printers.ToDictionary(p => p.Id, p => new PrinterWatch(p, scopeFactory, printerDispatcher));
    var heartbeats = printers.Select(p => new PrinterHeartbeat(p, scopeFactory, printerDispatcher));

    List<Task> tasks = [
      .. watches.Values.Select(w => w.RunAsync(ct)),
      RouteJobs(watches, ct),
      .. heartbeats.Select(h => h.RunAsync(ct)),
    ];

    await Task.WhenAll(tasks);
  }

  private async Task RouteJobs(IDictionary<Guid, PrinterWatch> watches, CancellationToken ct)
  {
    await foreach (var ctx in ippJobChannel.Reader.ReadAllAsync(ct))
    {
      if (watches.TryGetValue(ctx.PrinterId, out var w))
        w.AddJob(ctx.IppId, ctx.JobId);
    }
  }
}




