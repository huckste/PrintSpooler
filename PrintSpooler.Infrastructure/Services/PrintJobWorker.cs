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

      await jobService.UpdateJob(new JobUpdate(jobId, JobStatus.Submitting).NotifyDashboard(), ct);

      await jobService.GetJob(jobId)
        .ThenDoAsync(async j => await HandleProcessing(jobService, j, ct));
    }
  }

  private async Task RequeuePendingJobs()
  {
    using var scope = scopeFactory.CreateScope();
    var jobService = scope.ServiceProvider.GetRequiredService<IJobService>();

    await jobService.GetPendingJobs()
      .ThenDoAsync(async pending =>
        {
          foreach (var j in pending)
            await jobChannel.Writer.WriteAsync(j.Id);
        });
  }

  private async Task HandleProcessing(IJobService jobService, Job job, CancellationToken ct)
  {
    var result = await jobService.GetJobData(job.Id)
      .ThenAsync(data => printerDispatcher.SendAsync(job, data.Bytes, ct))
      .ThenDoAsync(async ippJob => await ippJobChannel.Writer.WriteAsync(ippJob));

    if (!result.IsError)
      return;

    var update = new JobUpdate(job.Id, JobStatus.Failed)
      .Log(JobAction.Failed, ByWho.System, result.Errors.First().Description)
      .NotifyDashboard();

    await jobService.UpdateJob(update, ct);
  }

}
