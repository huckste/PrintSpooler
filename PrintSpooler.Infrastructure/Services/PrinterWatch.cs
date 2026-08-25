namespace PrintSpooler.Infrastructure.Services;

using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using PrintSpooler.Core.Models;
using PrintSpooler.Core.Services;
using ErrorOr;
using Microsoft.Extensions.Logging;

public class PrinterWatch(
    Printer printer,
    IServiceScopeFactory scopeFactory,
    IPrinterDispatcher printerDispatcher,
    ILogger<PrinterPoller> logger) : IDisposable
{
  private static readonly TimeSpan Idle = TimeSpan.FromSeconds(60);
  private static readonly TimeSpan Active = TimeSpan.FromSeconds(5);

  private readonly ConcurrentDictionary<int, WatchedJob> _jobs = [];
  private readonly PeriodicTimer _timer = new(Idle);
  private bool _disposed;

  public void Dispose()
  {
    _timer.Dispose();
    _disposed = true;
  }

  public void AddJob(int ippId, Guid jobId)
  {
    ObjectDisposedException.ThrowIf(_disposed, this);

    _jobs.TryAdd(ippId, new WatchedJob(jobId, JobStatus.Unknown));
    _timer.Period = Active;
  }

  public async Task RunAsync(CancellationToken ct)
  {
    ObjectDisposedException.ThrowIf(_disposed, this);

    while (await _timer.WaitForNextTickAsync(ct))
    {
      try
      {
        await PollJobs(ct);
      }
      catch (Exception ex)
      {
        HandleErrors([Error.Unexpected("PollJobs.Exception", $"Ex: {ex.Message}")]);
      }
    }
  }

  private async Task PollJobs(CancellationToken ct)
  {
    if (_jobs.IsEmpty)
      _timer.Period = Idle;

    var result = await printerDispatcher
      .GetPrinterJobsAsync(printer, [.. _jobs.Keys], ct)
      .ThenAsync(value => HandleStateUpdate(value, ct));

    if (result.IsError)
      HandleErrors(result.Errors);
  }

  private void HandleErrors(List<Error> errors)
  {
    foreach (var e in errors)
      logger.LogError("{Printer}: {Code} - {Desription}", printer.Name, e.Code, e.Description);
  }

  private async Task<ErrorOr<Success>> HandleStateUpdate(List<IppJobStatus> ippJobs, CancellationToken ct)
  {
    using var scope = scopeFactory.CreateScope();
    var jobService = scope.ServiceProvider.GetRequiredService<IJobService>();

    List<Error> errors = [];

    foreach (var ippJob in ippJobs)
    {

      if (ippJob.Id is not { } ippId)
      {
        errors.Add(Error.NotFound("HandleStateUpdate.IppJobId", "Could not find id for ipp job"));
        continue;
      }

      if (!_jobs.TryGetValue(ippId, out var watched))
      {
        errors.Add(Error.NotFound("HandleStateUpdate.TryGetValue", $"Could not find value for ippId: {ippId}"));
        continue;
      }

      if (ippJob.State == JobStatus.Unknown)
      {
        errors.Add(Error.Failure("HandleStateUpdate.JobStatus", $"Unknown status for ippId: {ippId}"));
        continue;
      }

      if (ippJob.State == watched.State)
        continue;

      await jobService.GetJob(watched.JobId)
        .ThenDoAsync(async j =>
        {
          // Failed jobs will remain. need to store the ippJobId and make code to retry that job using the ippJobId
          if (JobPolicies.IsTerminal(ippJob.State))
            _jobs.TryRemove(ippId, out _);
          else
            _jobs[ippId] = new WatchedJob(watched.JobId, ippJob.State);

          JobAction? action = ippJob.State switch
          {
            JobStatus.Cancelled => JobAction.Cancelled,
            JobStatus.Completed => JobAction.Completed,
            JobStatus.Failed => JobAction.Failed,
            _ => null
          };

          var update = new JobUpdate(j.Id, ippJob.State).NotifyDashboard();

          if (action != null)
            update = update.Log((JobAction)action, ByWho.System, ippJob.Message);

          await jobService.UpdateJob(update, ct);
        });
    }

    return errors.Count > 0 ? errors : Result.Success;
  }

}
