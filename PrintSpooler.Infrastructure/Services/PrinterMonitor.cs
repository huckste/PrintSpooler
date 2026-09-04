using System.Collections.Concurrent;
using ErrorOr;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PrintSpooler.Core.Models;
using PrintSpooler.Core.Services;

namespace PrintSpooler.Infrastructure.Services;

public class PrinterMonitor : IAsyncDisposable

{
  private readonly ConcurrentDictionary<int, Guid> _cache = [];
  private readonly IServiceScopeFactory _scopeFactory;
  private readonly IPrinterDispatcher _printerDispatcher;
  private readonly CancellationTokenSource _cts;
  private readonly ILogger<PrinterMonitor> _logger;
  private readonly Task _loop;
  public readonly Guid printerId;

  public PrinterMonitor(
    Printer printer,
    CancellationToken hostToken,
    IServiceScopeFactory scopeFactory,
    IPrinterDispatcher printerDispatcher,
    ILogger<PrinterMonitor> logger)
  {
    printerId = printer.Id;
    _scopeFactory = scopeFactory;
    _printerDispatcher = printerDispatcher;
    _logger = logger;

    _cts = CancellationTokenSource.CreateLinkedTokenSource(hostToken);
    _loop = RunAsync(_cts.Token);
  }


  public void AddJob(int ippId, Guid jobId) => _cache.TryAdd(ippId, jobId);

  private async Task<ErrorOr<Success>> UpdatePrinterStatus(AsyncServiceScope scope, CancellationToken ct)
  {
    var printerStatus = await printerDispatcher.GetPrinterStatusAsync(printer, ct);

    if (printerStatus.IsError)
      return Error.NotFound("PrinterStatus.NotFound", $"No status was returned for printer: {printer.Name}");

    var printerService = scope.ServiceProvider.GetRequiredService<IPrinterService>();

    var result = await printerService.UpdatePrinterStatus(printer.Id, printerStatus.Value);

    return result.IsError ? result.Errors : Result.Success;
  }

  private async Task<ErrorOr<Success>> UpdateJobStatus(AsyncServiceScope scope, CancellationToken ct)
  {
    List<Error> errors = [];
    var ippJobs = await printerDispatcher.GetPrinterJobsAsync(printer, [.. _cache.Keys], ct);

    if (ippJobs.IsError)
      return ippJobs.Errors;

    var jobService = scope.ServiceProvider.GetRequiredService<IJobService>();
    var activeJobs = await jobService.GetActiveJobsByPrinter(printer.Id);

    foreach (var ippJob in ippJobs.Value)
    {
      if (ippJob.Id is not { } ippJobId)
      {
        errors.Add(Error.NotFound("IppJobId.NotFound", "IppJob came back as null"));
        continue;
      }

      if (!_cache.TryGetValue(ippJobId, out var jobId))
      {
        errors.Add(Error.NotFound("IppJobId.NotFound", $"No key was found matching ippId: {ippJobId}"));
        continue;
      }

      if (ippJob.State is not { } ippJobStatus)
      {
        errors.Add(Error.Failure("HandleStateUpdate.JobStatus", $"Unmodelled IPP state for ipp id: {ippJobId}"));
        continue;
      }

      if (activeJobs.FirstOrDefault(aj => aj.Id == jobId) is not { } job)
      {
        _cache.TryRemove(ippJobId, out _);
        errors.Add(Error.NotFound("HandleStateUpdate.Job", $"No job {jobId} behind ipp id {ippJobId}"));
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
        _cache.TryRemove(ippJobId, out _);

      var update = new JobUpdate(job.Id, ippJobStatus).NotifyDashboard();

      JobAction? action = ippJobStatus switch
      {
        JobStatus.Cancelled => JobAction.Cancelled,
        JobStatus.Completed => JobAction.Completed,
        JobStatus.Failed => JobAction.Failed,
        _ => null
      };

      if (action is { } jobAction)
        update = update.Log(jobAction, ByWho.System, ippJob.Message);

      var res = await jobService.UpdateJob(update, ct);

      if (res.IsError)
        errors.AddRange(res.Errors);
    }

    return errors.Count > 0 ? errors : Result.Success;
  }

  private async Task RunAsync(CancellationToken ct)
  {
    using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));

    while (await timer.WaitForNextTickAsync(ct))
    {
      await using var scope = scopeFactory.CreateAsyncScope();
      var printerResult = await UpdatePrinterStatus(scope, ct);

      if (printerResult.IsError)
        HandleErrors(printerResult.Errors);

      if (!_cache.IsEmpty)
      {
        var jobResult = await UpdateJobStatus(scope, ct);

        if (jobResult.IsError)
          HandleErrors(jobResult.Errors);
      }

    }
  }

  private void HandleErrors(List<Error> errors)
  {
    foreach (var e in errors)
      logger.LogError("{Printer}: {Code} - {Desription}", printer.Name, e.Code, e.Description);
  }

  public ValueTask DisposeAsync()
  {
    throw new NotImplementedException();
  }
}

