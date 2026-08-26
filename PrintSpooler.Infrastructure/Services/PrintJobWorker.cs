using System.Threading.Channels;
using ErrorOr;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PrintSpooler.Core.Models;
using PrintSpooler.Core.Services;

namespace PrintSpooler.Infrastructure.Services;

public class PrintJobWorker(
    Channel<Guid> jobChannel,
    Channel<IppJobRef> ippJobChannel,
    IPrinterDispatcher printerDispatcher,
    IServiceScopeFactory scopeFactory,
    ILogger<PrintJobWorker> logger
) : BackgroundService
{
  protected override async Task ExecuteAsync(CancellationToken ct)
  {

    await Init();

    await foreach (var jobId in jobChannel.Reader.ReadAllAsync(ct))
    {
      using var scope = scopeFactory.CreateScope();
      var jobService = scope.ServiceProvider.GetRequiredService<IJobService>();

      var res = await jobService
        .GetJob(jobId)
        .ThenAsync(j => HandleProcessing(jobService, j, ct));

      if (res.IsError)
        HandleErrors(res.Errors);
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
        {
          var res = await jobService.UpdateJob(
            new JobUpdate(job.Id, JobStatus.Failed)
            .Log(JobAction.Failed, ByWho.System, $"Job {job.Id} was in flight during API shutdown")
            .NotifyDashboard()
          );

          if (res.IsError)
            HandleErrors(res.Errors);
        }
      });
  }

  private void HandleErrors(List<Error> errors)
  {
    foreach (var e in errors)
      logger.LogError("{Code} - {Desription}", e.Code, e.Description);
  }

  private async Task<ErrorOr<Success>> HandleProcessing(IJobService jobService, Job job, CancellationToken ct)
  {
    List<Error> errors = [];

    var res = await JobPolicies
      .CanDispatch(job)
      .ThenEnsureAsync(async j =>
      {
        var res = await jobService
          .UpdateJob(new JobUpdate(job.Id, JobStatus.Submitting)
          .NotifyDashboard());

        return res.IsError ? res.Errors : j;
      })
      .ThenAsync(job => jobService.GetJobData(job.Id))
      .ThenAsync(jobData => printerDispatcher.SendAsync(job, jobData.Bytes, ct))
      .ThenDoAsync(async ippJob => await ippJobChannel.Writer.WriteAsync(ippJob));

    if (res.IsError)
    {
      errors.AddRange(res.Errors);

      var update = new JobUpdate(job.Id, JobStatus.Failed)
            .Log(JobAction.Failed, ByWho.System, res.Errors.First().Description)
            .NotifyDashboard();

      var updateRes = await jobService.UpdateJob(update, ct);

      if (updateRes.IsError)
        errors.AddRange(updateRes.Errors);
    }

    return errors.Count > 0 ? errors : Result.Success;

  }

}
