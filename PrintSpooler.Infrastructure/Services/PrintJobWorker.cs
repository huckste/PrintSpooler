using System.Threading.Channels;
using ErrorOr;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PrintSpooler.Core.Models;
using PrintSpooler.Core.Services;

namespace PrintSpooler.Infrastructure.Services;

public class PrintJobWorker(
    Channel<Guid> jobChannel,
    Channel<IppJobRef> ippJobChannel,
    IPrinterDispatcher printerDispatcher,
    IServiceScopeFactory scopeFactory
) : BackgroundService
{
  protected override async Task ExecuteAsync(CancellationToken ct)
  {
    await RequeuePendingJobs();

    await foreach (var jobId in jobChannel.Reader.ReadAllAsync(ct))
    {
      using var scope = scopeFactory.CreateScope();
      var jobService = scope.ServiceProvider.GetRequiredService<IJobService>();

      await jobService.GetJob(jobId)
        .ThenDoAsync(async j =>
        {
          if (j.Status == JobStatus.Cancelled)
            await jobService.RemoveJobData(jobId, ct);
          else
            await HandleProcessing(jobService, j, ct);
        });
    }
  }

  private async Task RequeuePendingJobs()
  {
    using var scope = scopeFactory.CreateScope();
    var jobService = scope.ServiceProvider.GetRequiredService<IJobService>();

    await jobService.GetPendingJobs().ThenDoAsync(async pj =>
    {
      foreach (var job in pj)
        await jobChannel.Writer.WriteAsync(job.Id);

    });
  }

  private async Task HandleProcessing(IJobService jobService, Job job, CancellationToken ct)
  {
    var result = await jobService.GetJobData(job.Id)
      .ThenAsync(jd => printerDispatcher.SendAsync(job, jd.Bytes, ct))
      .ThenDoAsync(async v => await ippJobChannel.Writer.WriteAsync(v));

    if (result.IsError)
      await jobService.UpdateJob(job.Id, JobStatus.Failed, JobAction.Failed, ByWho.System, result.Errors.First().Description, ct);
  }

}
