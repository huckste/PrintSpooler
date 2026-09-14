using System.Collections.Concurrent;
using System.Threading.Channels;
using ErrorOr;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PrintSpooler.Core.Models;
using PrintSpooler.Core.Services;

namespace PrintSpooler.Infrastructure.Services;

public class PrinterMonitor : IAsyncDisposable

{
  private readonly IServiceScopeFactory _scopeFactory;
  private readonly IPrinterDispatcher _printerDispatcher;
  private readonly CancellationTokenSource _cts;
  private readonly ILogger<PrinterMonitor> _logger;
  private readonly List<Task> _loops;
  private readonly ConcurrentDictionary<int, TrackedJob> _trackedJobs = [];
  public readonly Printer Printer;
  private readonly Channel<Guid> _queue;


  public PrinterMonitor(
    Printer printer,
    CancellationToken hostToken,
    IServiceScopeFactory scopeFactory,
    IPrinterDispatcher printerDispatcher,
    ILogger<PrinterMonitor> logger)
  {
    Printer = printer;
    _scopeFactory = scopeFactory;
    _printerDispatcher = printerDispatcher;
    _logger = logger;

    _cts = CancellationTokenSource.CreateLinkedTokenSource(hostToken);
    _queue = Channel.CreateUnbounded<Guid>();
    _loops = [RunAsync(_cts.Token), DispatchAsync(_cts.Token)];
  }

  public void Enqueue(Guid jobId) =>
    _queue.Writer.TryWrite(jobId);

  public ErrorOr<TrackedJob> AddJob(int ippId, Guid jobId)
  {
    var trackedJob = new TrackedJob(jobId, new(), 0);

    if (_trackedJobs.TryAdd(ippId, trackedJob))
      return trackedJob;

    return Error.Conflict("AddJob.Conflict", $"Unable to add ippJob {ippId} to trackedJobs");
  }

  private async Task<ErrorOr<Success>> UpdatePrinterStatus(AsyncServiceScope scope, CancellationToken ct)
  {
    var printerStatus = await _printerDispatcher.GetPrinterStatusAsync(Printer, ct);

    if (printerStatus.IsError)
      return Error.NotFound("PrinterStatus.NotFound", $"No status was returned for printer: {Printer.Name}");

    var printerService = scope.ServiceProvider.GetRequiredService<IPrinterService>();

    var result = await printerService.UpdatePrinterStatus(Printer.Id, printerStatus.Value.Status, printerStatus.Value.Reason, printerStatus.Value.UpTimeSeconds);

    return result.IsError ? result.Errors : Result.Success;
  }

  private const int MaxMissedPolls = 3;

  private async Task<ErrorOr<Success>> UpdateJobStatus(AsyncServiceScope scope, CancellationToken ct)
  {
    List<Error> errors = [];
    var ippJobs = await _printerDispatcher.GetPrinterJobsAsync(Printer, [.. _trackedJobs.Keys], ct);

    if (ippJobs.IsError)
      return ippJobs.Errors;

    var jobService = scope.ServiceProvider.GetRequiredService<IJobService>();
    var activeJobs = await jobService.GetActiveJobsByPrinter(Printer.Id);
    HashSet<int> reportedIds = [];

    foreach (var ippJob in ippJobs.Value)
    {
      // Printer unable to find ippJobId : which job has this ippJobId?
      if (ippJob.Id is not { } ippJobId)
      {
        errors.Add(Error.NotFound("IppJobId.NotFound", "IppJob came back as null"));
        continue;
      }

      reportedIds.Add(ippJobId);

      // Printer returned unknown ippJobId
      if (!_trackedJobs.TryGetValue(ippJobId, out var trackedJob))
      {
        errors.Add(Error.NotFound("IppJobId.NotFound", $"No key was found matching ippId: {ippJobId}"));
        continue;
      }

      // The printer mentioned it again - the miss streak is over
      if (trackedJob.MissedPolls > 0)
      {
        trackedJob = trackedJob with { MissedPolls = 0 };
        _trackedJobs[ippJobId] = trackedJob;
      }

      // Printer returned unknown ippJob status
      if (ippJob.State is not { } ippJobStatus)
      {
        errors.Add(Error.Failure("HandleStateUpdate.JobStatus", $"Unmodelled IPP state for ipp id: {ippJobId}"));
        continue;
      }

      // no active job contains ippJobId returned by printer
      if (activeJobs.FirstOrDefault(aj => aj.Id == trackedJob.Id) is not { } job)
      {
        _trackedJobs.TryRemove(ippJobId, out _);
        errors.Add(Error.NotFound("HandleStateUpdate.Job", $"No job {trackedJob.Id} behind ipp id {ippJobId}"));
        continue;
      }

      // We have asked the printer to cancel and are waiting on it to confirm
      if (job.Status is JobStatus.Cancelling && JobPolicies.IsInFlight(ippJobStatus))
        continue;

      // no need to update if status has not changed
      if (ippJobStatus == job.Status)
        continue;

      // The printer is done - no longer reports as in flight
      if (!JobPolicies.IsInFlight(ippJobStatus))
      {
        if (_trackedJobs.TryRemove(ippJobId, out _))
          trackedJob.CompletionSource.TrySetResult(ippJobStatus);
      }

      var update = new JobUpdate(job.Id, ippJobStatus).NotifyDashboard();

      if (ActionFor(ippJobStatus) is { } jobAction)
        update = update.Log(jobAction, ByWho.System, ippJob.Message);

      var res = await jobService.UpdateJob(update, ct);

      if (res.IsError)
        errors.AddRange(res.Errors);
    }

    foreach (var missingId in _trackedJobs.Keys.Except(reportedIds).ToList())
    {
      if (!_trackedJobs.TryGetValue(missingId, out var trackedJob))
        continue;

      if (trackedJob.MissedPolls + 1 < MaxMissedPolls)
      {
        _trackedJobs[missingId] = trackedJob with { MissedPolls = trackedJob.MissedPolls + 1 };
        continue;
      }

      if (_trackedJobs.TryRemove(missingId, out _))
        trackedJob.CompletionSource.TrySetResult(JobStatus.Failed);

      var missedUpdate = new JobUpdate(trackedJob.Id, JobStatus.Failed)
        .Log(JobAction.Failed, ByWho.System, $"Printer stopped reporting this job after {MaxMissedPolls} consecutive polls")
        .NotifyDashboard();

      var missedRes = await jobService.UpdateJob(missedUpdate, ct);

      if (missedRes.IsError)
        errors.AddRange(missedRes.Errors);
    }

    return errors.Count > 0 ? errors : Result.Success;
  }

  private static JobAction? ActionFor(JobStatus status) => status switch
  {
    JobStatus.Cancelled => JobAction.Cancelled,
    JobStatus.Completed => JobAction.Completed,
    JobStatus.Failed => JobAction.Failed,
    _ => null
  };

  private async Task RecoverAsync(CancellationToken ct)
  {
    using var scope = _scopeFactory.CreateScope();
    var jobService = scope.ServiceProvider.GetRequiredService<IJobService>();

    var jobs = await jobService.GetActiveJobsByPrinter(Printer.Id);
    var lingering = jobs.Where(j => JobPolicies.Pending.Contains(j.Status) || JobPolicies.InFlight.Contains(j.Status));

    foreach (var job in lingering)
    {
      if (job.IppJobId is not { } ippJobId)
      {
        await FailLingeringJob(jobService, job, "Job was queued but never reached the printer before restart", ct);
        continue;
      }

      var ippJobs = await _printerDispatcher.GetPrinterJobsAsync(Printer, [ippJobId], ct);

      if (ippJobs.IsError || ippJobs.Value.FirstOrDefault(j => j.Id == ippJobId) is not { State: { } state } ippJob)
      {
        await FailLingeringJob(jobService, job, "Could not confirm job state with the printer after restart", ct);
        continue;
      }

      if (!JobPolicies.IsInFlight(state))
      {
        var update = new JobUpdate(job.Id, state).NotifyDashboard();

        if (ActionFor(state) is { } action)
          update = update.Log(action, ByWho.System, ippJob.Message);

        var res = await jobService.UpdateJob(update, ct);

        if (res.IsError)
          HandleErrors(res.Errors);

        continue;
      }

      var completionRes = await AddJob(ippJobId, job.Id).ThenAsync(t => t.CompletionSource.Task);

      if (completionRes.IsError)
        HandleErrors(completionRes.Errors);
    }
  }

  private async Task FailLingeringJob(IJobService jobService, Job job, string reason, CancellationToken ct)
  {
    var update = new JobUpdate(job.Id, JobStatus.Failed)
      .Log(JobAction.Failed, ByWho.System, reason)
      .NotifyDashboard();

    var res = await jobService.UpdateJob(update, ct);

    if (res.IsError)
      HandleErrors(res.Errors);
  }

  private async Task DispatchAsync(CancellationToken ct)
  {
    await RecoverAsync(ct);

    await foreach (var jobId in _queue.Reader.ReadAllAsync(ct))
    {
      using var scope = _scopeFactory.CreateScope();
      var jobService = scope.ServiceProvider.GetRequiredService<IJobService>();

      var res = await jobService
        .GetJob(jobId)
        .ThenAsync(j => HandleProcessing(jobService, j, ct));

      if (res.IsError)
        HandleErrors(res.Errors);
    }
  }

  private async Task<ErrorOr<Success>> HandleProcessing(IJobService jobService, Job job, CancellationToken ct)
  {
    List<Error> errors = [];

    var dispatchRes = await JobPolicies
      .CanDispatch(job)
      .ThenEnsureAsync(async j =>
      {
        var updateRes = await jobService
          .UpdateJob(new JobUpdate(job.Id, JobStatus.Submitting)
          .NotifyDashboard());

        return updateRes.IsError ? updateRes.Errors : j;
      })
      .ThenAsync(job => jobService.GetJobData(job.Id))
      .ThenAsync(jobData => _printerDispatcher.SendAsync(job, jobData.Bytes, ct))
      .ThenDoAsync(ippJob => jobService.SetIppJobId(ippJob.JobId, ippJob.IppId, ct));

    if (dispatchRes.IsError)
    {
      errors.AddRange(dispatchRes.Errors);

      var update = new JobUpdate(job.Id, JobStatus.Failed)
              .Log(JobAction.Failed, ByWho.System, dispatchRes.Errors.First().Description)
              .NotifyDashboard();

      var updateRes = await jobService.UpdateJob(update, ct);

      if (updateRes.IsError)
        errors.AddRange(updateRes.Errors);
    }
    else
    {
      var completionRes = await AddJob(dispatchRes.Value.IppId, dispatchRes.Value.JobId)
        .ThenAsync(t => t.CompletionSource.Task);

      if (completionRes.IsError)
        errors.AddRange(completionRes.Errors);
    }

    return errors.Count > 0 ? errors : Result.Success;
  }

  private async Task RunAsync(CancellationToken ct)
  {
    var idle = TimeSpan.FromSeconds(30);
    var active = TimeSpan.FromSeconds(5);
    var extendedIdle = TimeSpan.FromSeconds(60);

    using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));

    while (await timer.WaitForNextTickAsync(ct))
    {
      await using var scope = _scopeFactory.CreateAsyncScope();
      var printerResult = await UpdatePrinterStatus(scope, ct);

      if (!printerResult.IsError)
      {

        if (!_trackedJobs.IsEmpty)
        {
          timer.Period = active;

          var jobResult = await UpdateJobStatus(scope, ct);

          if (jobResult.IsError)
            HandleErrors(jobResult.Errors);
        }
        else
        {
          timer.Period = idle;
        }

      }
      else
      {
        HandleErrors(printerResult.Errors);

        if (printerResult.FirstError.Code == "PrinterStatus.NotFound")
          timer.Period = extendedIdle;
      }

    }
  }

  private void HandleErrors(List<Error> errors)
  {
    foreach (var e in errors)
      _logger.LogError("{Printer}: {Code} - {Desription}", Printer.Name, e.Code, e.Description);
  }

  public async ValueTask DisposeAsync()
  {
    _cts.Cancel();

    try
    {
      foreach (var loop in _loops)
        await loop;
    }
    catch (OperationCanceledException ex)
    {
      _logger.LogError(ex.Message);
    }
    catch (Exception ex)
    {
      _logger.LogError($"Error during printer monitor disposal: {ex.Message}");
    }
    finally
    {
      _cts.Dispose();
    }
  }
}

