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
      return Error.Failure("PrintJobResponse.Reject", $"IPP error: {ex.Message}");
    }
    catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
    {
      return Error.Unexpected("PrintJobResponse.Transport", $"Could not reach printer at {host}: {ex.Message}");
    }

    if (response.JobAttributes?.JobId is not { } id)
      return Error.Failure("JobAttributes.Id", "Job returned no Id");

    return new IppJobRef(job.PrinterId, job.Id, id);
  }

  public async Task<ErrorOr<List<IppJobStatus>>> GetPrinterJobsAsync(Printer printer, int[] ids, CancellationToken ct)
  {
    if (printer.Host is not { } host)
      return Error.NotFound("GetPrinterJobsAsync.Host", $"Could not find host for printer: {printer.Name}");

    GetJobsResponse? response;
    List<IppJobStatus> ippJobs = [];

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

      response = await _client.GetJobsAsync(request, ct);
    }
    catch (IppResponseException ex)
    {
      return Error.Failure("GetJobsResponse.Reject", $"IPP error: {ex.Message}");
    }
    catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
    {
      return Error.Unexpected("GetJobsResponse.Transport", $"Could not reach printer at {host}: {ex.Message}");
    }

    if (response?.JobsAttributes is not { } attributes)
      return Error.Failure("JobsAttributes.Failure", "Job returned no attributes");

    ippJobs = [..attributes.Select(j => new IppJobStatus
        {
          Id = j.JobId,
          State = j.JobState switch
          {
            JobState.Pending => JobStatus.Queued,
            JobState.PendingHeld => JobStatus.Queued,
            JobState.Processing => JobStatus.Processing,
            JobState.ProcessingStopped => JobStatus.Failed,
            JobState.Canceled => JobStatus.Cancelled,
            JobState.Aborted => JobStatus.Failed,
            JobState.Completed => JobStatus.Completed,
            _ => JobStatus.Unknown
          },
          Message = j.JobStateMessage,
        })];

    return ippJobs;
  }

  public async Task<ErrorOr<PrinterStatus>> GetPrinterStatusAsync(Printer printer, CancellationToken ct)
  {
    if (printer.Host is not { } host)
      return Error.NotFound("GetPrinterStatusAsync.Host", $"Could not find host for printer: {printer.Name}");

    GetPrinterAttributesResponse? response;

    try
    {
      var request = new GetPrinterAttributesRequest
      {
        OperationAttributes = new()
        {
          PrinterUri = new Uri($"ipp://{host}:631/ipp/print"),
          RequestedAttributes = ["printer-state", "printer-state-reasons"],
        }
      };

      response = await _client.GetPrinterAttributesAsync(request, ct);

    }
    catch (IppResponseException ex)
    {
      return Error.Failure("GetPrinterAttributesResponse.Reject", $"IPP error: {ex.Message}");
    }
    catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
    {
      return PrinterStatus.Offline;
    }

    if (response?.PrinterAttributes?.PrinterState is not { } state)
      return Error.Failure("PrinterState.Failure", "Printer returned no state");

    return state switch
    {
      PrinterState.Idle => PrinterStatus.Idle,
      PrinterState.Processing => PrinterStatus.Processing,
      PrinterState.Stopped => PrinterStatus.Stopped,
      _ => Error.Failure("PrinterState.Failure", $"Unknown printer state: {state}"),
    };
  }
}
