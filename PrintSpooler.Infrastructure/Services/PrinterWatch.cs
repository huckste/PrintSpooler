namespace PrintSpooler.Infrastructure.Services;

using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using PrintSpooler.Core.Models;
using PrintSpooler.Core.Services;
using ErrorOr;

public class PrinterWatch(Printer printer, IServiceScopeFactory scopeFactory, IPrinterDispatcher printerDispatcher)
{
  private static readonly TimeSpan Idle = TimeSpan.FromSeconds(60);
  private static readonly TimeSpan Active = TimeSpan.FromSeconds(5);

  private readonly ConcurrentDictionary<int, WatchedJob> _jobs = [];
  private readonly PeriodicTimer _timer = new(Idle);

  public void AddJob(int ippId, Guid jobId)
  {
    _jobs.TryAdd(ippId, new WatchedJob(jobId, JobStatus.Submitting));
    _timer.Period = Active;
  }

  public async Task RunAsync(CancellationToken ct)
  {
    while (await _timer.WaitForNextTickAsync(ct))
    {
      try
      {
        await PollJobs(ct);
      }
      catch (Exception ex)
      {
        Console.WriteLine($"PollJobs.Exception: {ex.Message}");
      }
    }
  }

  private async Task PollJobs(CancellationToken ct)
  {
    if (_jobs.IsEmpty)
      _timer.Period = Idle;

    await printerDispatcher.GetPrinterJobsAsync(printer, [.. _jobs.Keys]).Switch(
        async value => await HandleStateUpdate(value, ct),
         HandleErrors
       );
  }

  private void HandleErrors(List<Error> errors)
  {
    foreach (var error in errors)
      Console.WriteLine($"PollJobs.Error: {error.Description}");
  }

  private async Task HandleStateUpdate(List<IppJobStatus> ippJobs, CancellationToken ct)
  {
    using var scope = scopeFactory.CreateScope();
    var jobService = scope.ServiceProvider.GetRequiredService<IJobService>();

    foreach (var ippJob in ippJobs)
    {

      if (ippJob.Id == null)
        continue;

      int ippId = (int)ippJob.Id;

      if (!_jobs.TryGetValue(ippId, out var watched))
        continue;

      if (ippJob.State == watched.State || ippJob.State == JobStatus.Unknown)
        continue;

      await jobService.GetJob(watched.JobId)
        .ThenDo(j =>
        {
          if (ippJob.State is JobStatus.Failed or JobStatus.Cancelled or JobStatus.Completed)
            _jobs.TryRemove(ippId, out _);
          else
            _jobs[ippId] = new WatchedJob(watched.JobId, ippJob.State);
        })
        .ThenDoAsync(async j =>
        {
          j.Status = ippJob.State;
          j.FailureReason = ippJob.Message ?? "";

          JobAction? action = ippJob.State switch
          {
            JobStatus.Cancelled => JobAction.Cancelled,
            JobStatus.Completed => JobAction.Completed,
            JobStatus.Failed => JobAction.Failed,
            _ => null
          };

          if (action != null)
            await jobService.UpdateJob(j, (JobAction)action, ByWho.System, ct);
        });
    }
  }
}
