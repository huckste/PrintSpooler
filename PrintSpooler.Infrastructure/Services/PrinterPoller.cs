using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PrintSpooler.Core.Models;
using PrintSpooler.Core.Services;

namespace PrintSpooler.Infrastructure.Services;

public class PrinterPoller(
    IServiceScopeFactory scopeFactory,
    Channel<IppJobRef> ippJobChannel,
    IPrinterDispatcher printerDispatcher,
    ILogger<PrinterWatch> printerWatchLog,
    ILogger<PrinterHeartbeat> printerHeartbeatLog,
    ILogger<PrinterPoller> logger) : BackgroundService
{
  protected override async Task ExecuteAsync(CancellationToken ct)
  {
    List<Printer> printers;
    Dictionary<Guid, PrinterWatch> watches = [];
    List<PrinterHeartbeat> heartbeats = [];

    try
    {
      using (var scope = scopeFactory.CreateScope())
      {
        var printerService = scope.ServiceProvider.GetRequiredService<IPrinterService>();
        printers = await printerService.GetPrinters();
      }

      watches = printers.ToDictionary(p => p.Id, p => new PrinterWatch(p, scopeFactory, printerDispatcher, printerWatchLog));
      heartbeats = [.. printers.Select(p => new PrinterHeartbeat(p, scopeFactory, printerDispatcher, printerHeartbeatLog))];

      List<Task> tasks = [
        .. watches.Values.Select(w => w.RunAsync(ct)),
      RouteJobs(watches, ct),
      .. heartbeats.Select(h => h.RunAsync(ct)),
    ];

      await Task.WhenAll(tasks);
    }
    finally
    {
      foreach (var w in watches.Values)
        w.Dispose();

      foreach (var h in heartbeats)
        h.Dispose();
    }
  }

  private async Task RouteJobs(Dictionary<Guid, PrinterWatch> watches, CancellationToken ct)
  {
    await foreach (var ctx in ippJobChannel.Reader.ReadAllAsync(ct))
    {
      if (watches.TryGetValue(ctx.PrinterId, out var w))
      {
        w.AddJob(ctx.IppId, ctx.JobId);
        continue;
      }

      // No watch for that printer, so nothing will ever resolve this job.
      // Dropping it silently would leave the row in flight forever.
      logger.LogError(
        "No watch for printer {PrinterId}; failing job {JobId}", ctx.PrinterId, ctx.JobId);

      using var scope = scopeFactory.CreateScope();
      var jobService = scope.ServiceProvider.GetRequiredService<IJobService>();

      var res = await jobService.UpdateJob(
        new JobUpdate(ctx.JobId, JobStatus.Failed)
          .Log(JobAction.Failed, ByWho.System, $"No printer watch for {ctx.PrinterId}")
          .NotifyDashboard(), ct);

      if (res.IsError)
        foreach (var e in res.Errors)
          logger.LogError("{Code} - {Description}", e.Code, e.Description);
    }
  }
}
