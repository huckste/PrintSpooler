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

    await Init();

    await foreach (var jobId in jobChannel.Reader.ReadAllAsync(ct))
    {
      using var scope = scopeFactory.CreateScope();
      var jobService = scope.ServiceProvider.GetRequiredService<IJobService>();

      await jobService
        .GetJob(jobId)
        .ThenDoAsync(j => HandleProcessing(jobService, j, ct));
    }
  }

  private async Task Init()
  {
    using var scope = scopeFactory.CreateScope();
    var jobService = scope.ServiceProvider.GetRequiredService<IJobService>();

    // requeue pending jobs
    await jobService
      .GetPendingJobs()
      .ThenDoAsync(async pending =>
        {
          foreach (var j in pending)
            await jobChannel.Writer.WriteAsync(j.Id);
        });

    // set in flight jobs to failed 
    await jobService
      .GetMiaJobs()
      .ThenDoAsync(async miaJobs =>
      {
        foreach (var job in miaJobs)
          await jobService
            .UpdateJob(
              new JobUpdate(job.Id, JobStatus.Failed)
                .Log(JobAction.Failed, ByWho.System, $"Job {job.Id} was in flight during API crash")
                .NotifyDashboard()
            );
      });

  }

  private async Task HandleProcessing(IJobService jobService, Job job, CancellationToken ct)
  {
    var res = await JobPolicies
      .CanDispatch(job)
      .ThenDoAsync(j => jobService
        .UpdateJob(new JobUpdate(job.Id, JobStatus.Submitting)
        .NotifyDashboard()))
      .ThenAsync(job => jobService.GetJobData(job.Id))
      .ThenAsync(jobData => printerDispatcher.SendAsync(job, jobData.Bytes, ct))
      .ThenDoAsync(async ippJob => await ippJobChannel.Writer.WriteAsync(ippJob));

    if (res.IsError)
    {
      var update = new JobUpdate(job.Id, JobStatus.Failed)
            .Log(JobAction.Failed, ByWho.System, res.Errors.First().Description)
            .NotifyDashboard();

      await jobService.UpdateJob(update, ct);
    }

  }

}
