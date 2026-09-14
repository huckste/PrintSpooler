using ErrorOr;
using PrintSpooler.Core.Models;
using PrintSpooler.Core.Services;
using SharpIpp;
using SharpIpp.Exceptions;
using SharpIpp.Models.Requests;
using SharpIpp.Models.Responses;
using SharpIpp.Protocol.Models;

namespace PrintSpooler.Infrastructure.Services;

public class PrinterDispatcher(SharpIppClient client) : IPrinterDispatcher
{
  private readonly SharpIppClient _client = client;

  // Bounds a single poll round-trip (status or job-list check) so a printer
  // that accepts a connection and never answers can't hang its monitor's
  // loop indefinitely — the host token alone only fires on app shutdown,
  // which isn't a bound on any one operation. Scoped to polling specifically
  // (not send/cancel), matching the old per-printer PrinterWatch's PollTimeout.
  private static readonly TimeSpan PollTimeout = TimeSpan.FromSeconds(10);

  public async Task<ErrorOr<IppJobRef>> SendAsync(Job job, byte[]? jobData, CancellationToken ct)
  {
    if (job.Printer is not { } printer)
      return Error.NotFound("SendAsync.Printer", $"Could not find a printer for job id: {job.Id}");

    if (jobData is not { } data)
      return Error.NotFound("SendAsync.JobData", $"Could not find data for job id: {job.Id}");

    if (printer.Host is not { } host)
      return Error.NotFound("SendAsync.Host", $"Could not find Host for printer: {job.Printer.Name} ");

    PrintJobResponse? response;

    try
    {
      using var stream = new MemoryStream(data);

      var request = new PrintJobRequest
      {
        OperationAttributes = new()
        {
          PrinterUri = new Uri($"ipp://{host}:631/ipp/print"),
          JobName = job.FileName,
          DocumentFormat = job.ContentType,
        },

        Document = stream,
      };

      response = await _client.PrintJobAsync(request, ct);

    }
    catch (IppResponseException ex)
    {
      return Error.Failure("PrintJobResponse.Reject", $"IPP error: {ex.ResponseMessage.StatusCode}");
    }
    catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
    {
      return Error.Unexpected("PrintJobResponse.Transport", $"Could not reach printer at {host}: {ex.Message}");
    }

    if (response.JobAttributes?.JobId is not { } id)
      return Error.NotFound("JobAttributes.Id", "Job returned no Id");

    return new IppJobRef(job.PrinterId, job.Id, id);
  }

  public async Task<ErrorOr<Success>> CancelPrinterJob(Printer? printer, int? id, CancellationToken ct)
  {
    if (printer is null)
      return Error.NotFound("CancelPrinterJob.Printer", $"Could not find a printer for ipp id: {id}");

    if (printer.Host is not { } host)
      return Error.NotFound("GetPrinterJobsAsync.Host", $"Could not find host for printer: {printer.Name}");

    CancelJobResponse? response;

    try
    {
      var request = new CancelJobRequest
      {
        OperationAttributes = new()
        {
          PrinterUri = new Uri($"ipp://{host}:631/ipp/print"),
          JobId = id
        }
      };

      response = await _client.CancelJobAsync(request, ct);
    }
    catch (IppResponseException ex)
    {
      return Error.Failure("CancelJobResponse.Reject", $"IPP error: {ex.ResponseMessage.StatusCode}");
    }
    catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
    {
      return Error.Unexpected("CancelJobResponse.Transport", $"Could not reach printer at {host}: {ex.Message}");
    }

    return response?.StatusCode == IppStatusCode.SuccessfulOk
      ? Result.Success
      : Error.Failure("CancelJobResponse.Failure", $"Unable to cancel job: {response?.StatusCode}");
  }

  public async Task<ErrorOr<List<IppJobStatus>>> GetPrinterJobsAsync(Printer printer, int[] ids, CancellationToken ct)
  {
    if (printer.Host is not { } host)
      return Error.NotFound("GetPrinterJobsAsync.Host", $"Could not find host for printer: {printer.Name}");

    GetJobsResponse? response;
    List<IppJobStatus> ippJobs = [];

    using var pollCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    pollCts.CancelAfter(PollTimeout);

    try
    {
      var request = new GetJobsRequest
      {
        OperationAttributes = new()
        {
          PrinterUri = new Uri($"ipp://{host}:631/ipp/print"),
          JobIds = ids,
          RequestedAttributes = ["job-id", "job-uri", "job-state", "job-state-reasons", "job-state-message"]
        }
      };

      response = await _client.GetJobsAsync(request, pollCts.Token);
    }
    catch (IppResponseException ex)
    {
      return Error.Failure("GetJobsResponse.Reject", $"IPP error: {ex.ResponseMessage.StatusCode}");
    }
    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
    {
      return Error.Failure("GetJobsResponse.Timeout", $"No response from {host} within {PollTimeout.TotalSeconds}s");
    }
    catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
    {
      return Error.Unexpected("GetJobsResponse.Transport", $"Could not reach printer at {host}: {ex.Message}");
    }

    if (response is null)
      return Error.Failure("GetJobsResponse.Empty", $"No response from printer {printer.Name}");

    // The printer answered and knows none of these jobs and should not be marked as failure
    if (response.JobsAttributes is not { } attributes)
      return ippJobs;

    foreach (var a in attributes)
    {
      JobStatus? status = a.JobState switch
      {
        JobState.Pending => JobStatus.Submitting,
        JobState.PendingHeld => JobStatus.Submitting,
        JobState.Processing => JobStatus.Processing,
        JobState.ProcessingStopped => JobStatus.Stopped,
        JobState.Canceled => JobStatus.Cancelled,
        JobState.Aborted => JobStatus.Failed,
        JobState.Completed => JobStatus.Completed,
        _ => null
      };

      ippJobs.Add(new IppJobStatus
      {
        Id = a.JobId,
        State = status,
        Message = string.IsNullOrEmpty(a.JobStateMessage)
          ? string.Join(", ", (a.JobStateReasons ?? []).Where(r => r != JobStateReason.None).Distinct())
          : a.JobStateMessage,
      });
    }

    return ippJobs;
  }

  public async Task<ErrorOr<PrinterStatusReport>> GetPrinterStatusAsync(Printer printer, CancellationToken ct)
  {
    if (printer.Host is not { } host)
      return Error.NotFound("GetPrinterStatusAsync.Host", $"Could not find host for printer: {printer.Name}");

    GetPrinterAttributesResponse? response;

    using var pollCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    pollCts.CancelAfter(PollTimeout);

    try
    {
      var request = new GetPrinterAttributesRequest
      {
        OperationAttributes = new()
        {
          PrinterUri = new Uri($"ipp://{host}:631/ipp/print"),
          RequestedAttributes = ["printer-state", "printer-state-reasons", "printer-up-time"],
        }
      };

      response = await _client.GetPrinterAttributesAsync(request, pollCts.Token);

    }
    catch (IppResponseException ex)
    {
      return Error.Failure("GetPrinterAttributesResponse.Reject", $"IPP error: {ex.ResponseMessage.StatusCode}");
    }
    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
    {
      return new PrinterStatusReport(PrinterStatus.Offline, $"No response within {PollTimeout.TotalSeconds}s");
    }
    catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
    {
      return new PrinterStatusReport(PrinterStatus.Offline, ex.Message);
    }

    if (response?.PrinterAttributes?.PrinterState is not { } state)
      return Error.Failure("PrinterState.Failure", "Printer returned no state");

    var reasons = (response.PrinterAttributes.PrinterStateReasons ?? [])
      .Where(r => r != PrinterStateReason.None)
      .Distinct()
      .ToArray();

    string? reason = reasons.Length > 0 ? string.Join(", ", reasons) : null;
    var upTime = response.PrinterAttributes.PrinterUpTime;

    return state switch
    {
      PrinterState.Idle => new PrinterStatusReport(PrinterStatus.Idle, reason, upTime),
      PrinterState.Processing => new PrinterStatusReport(PrinterStatus.Processing, reason, upTime),
      PrinterState.Stopped => new PrinterStatusReport(PrinterStatus.Stopped, reason, upTime),
      _ => Error.Failure("PrinterState.Failure", $"Unknown printer state: {state}"),
    };
  }

}
