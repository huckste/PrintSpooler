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
    ILogger<PrinterWatch> logger) : IDisposable
{
  private static readonly TimeSpan Idle = TimeSpan.FromSeconds(60);
  private static readonly TimeSpan Active = TimeSpan.FromSeconds(5);
  private static readonly TimeSpan PollTimeout = TimeSpan.FromSeconds(10);

  // Consecutive polls a printer may answer without mentioning a job before giving up
  private const int MaxMissedPolls = 3;

  private readonly ConcurrentDictionary<int, WatchedJob> _jobs = [];
  private readonly PeriodicTimer _timer = new(Idle);

  public void Dispose()
  {
    _timer.Dispose();
  }

  public void AddJob(int ippId, Guid jobId)
  {
    _jobs.TryAdd(ippId, new WatchedJob(jobId));
    _timer.Period = Active;
  }

  public async Task RunAsync(CancellationToken ct)
  {
    while (await _timer.WaitForNextTickAsync(ct))
    {
      // Without this an unresponsive printer stalls the loop indefinitely, and holds up shutdown.
      using var pollCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
      pollCts.CancelAfter(PollTimeout);

      try
      {
        await PollJobs(pollCts.Token);
      }
      catch (OperationCanceledException) when (!ct.IsCancellationRequested)
      {
        HandleErrors([Error.Failure("PrinterWatch.Timeout", $"No job from {printer.Name} within {PollTimeout.TotalSeconds}s")]);
      }
      catch (Exception ex) when (ex is not OperationCanceledException)
      {
        HandleErrors([Error.Unexpected("PollJobs.Exception", $"Ex: {ex.Message}")]);
      }
    }
  }

  private async Task PollJobs(CancellationToken ct)
  {
    logger.LogInformation("{method}: {operation}", "PollJobs", "Start");
    if (_jobs.IsEmpty)
    {
      _timer.Period = Idle;
      return;
    }

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

    var tracked = _jobs.ToArray();

    List<Job> jobs = await jobService.GetJobs([.. tracked.Select(t => t.Value.JobId)], ct);
    var byJobId = jobs.ToDictionary(j => j.Id);
    var reported = ippJobs
      .Where(j => j.Id is not null)
      .ToDictionary(j => j.Id!.Value);

    foreach (var (ippId, watched) in tracked)
    {
      if (!byJobId.TryGetValue(watched.JobId, out var job))
      {
        _jobs.TryRemove(ippId, out _);
        errors.Add(Error.NotFound("HandleStateUpdate.Job", $"No job {watched.JobId} behind ipp id {ippId}"));
        continue;
      }

      if (!reported.TryGetValue(ippId, out var ippJob))
      {
        var missing = await HandleMissingJob(jobService, ippId, watched, job, ct);

        if (missing.IsError)
          errors.AddRange(missing.Errors);

        continue;
      }

      _jobs[ippId] = watched with { MissedPolls = 0 };

      if (ippJob.State is not { } state)
      {
        errors.Add(Error.Failure("HandleStateUpdate.JobStatus", $"Unmodelled IPP state for ipp id: {ippId}"));
        continue;
      }

      // We have asked the printer to cancel and are waiting on it to confirm
      if (job.Status is JobStatus.Cancelling && JobPolicies.IsInFlight(state))
        continue;

      if (state == job.Status)
        continue;

      // The printer is done - no longer reports as in flight
      if (!JobPolicies.IsInFlight(state))
        _jobs.TryRemove(ippId, out _);

      JobAction? action = state switch
      {
        JobStatus.Cancelled => JobAction.Cancelled,
        JobStatus.Completed => JobAction.Completed,
        JobStatus.Failed => JobAction.Failed,
        _ => null
      };

      var update = new JobUpdate(job.Id, state).NotifyDashboard();

      if (action is { } jobAction)
        update = update.Log(jobAction, ByWho.System, ippJob.Message);

      var res = await jobService.UpdateJob(update, ct);

      if (res.IsError)
        errors.AddRange(res.Errors);
    }

    return errors.Count > 0 ? errors : Result.Success;
  }

  // The printer answered but said nothing about this job, so it is gone from the printer's queue
  private async Task<ErrorOr<Success>> HandleMissingJob(
    IJobService jobService,
    int ippId,
    WatchedJob watched,
    Job job,
    CancellationToken ct)
  {
    // Already resolved by some other path
    if (!JobPolicies.IsInFlight(job.Status))
    {
      _jobs.TryRemove(ippId, out _);
      return Result.Success;
    }

    var missed = watched.MissedPolls + 1;

    if (missed < MaxMissedPolls)
    {
      _jobs[ippId] = watched with { MissedPolls = missed };
      return Result.Success;
    }

    _jobs.TryRemove(ippId, out _);

    var (status, action) = job.Status is JobStatus.Cancelling
      ? (JobStatus.Cancelled, JobAction.Cancelled)
      : (JobStatus.Failed, JobAction.Failed);

    return await jobService.UpdateJob(
      new JobUpdate(job.Id, status)
        .Log(action, ByWho.System,
             $"{printer.Name} stopped reporting IPP job {ippId} after {missed} polls")
        .NotifyDashboard(), ct);
  }

}
