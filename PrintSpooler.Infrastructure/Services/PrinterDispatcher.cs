using ErrorOr;
using PrintSpooler.Core.Models;
using PrintSpooler.Core.Services;
using SharpIpp;
using SharpIpp.Models.Requests;
using SharpIpp.Protocol.Models;

namespace PrintSpooler.Infrastructure.Services;

public class PrinterDispatcher : IPrinterDispatcher
{
  private readonly SharpIppClient _client = new();

  public async Task<ErrorOr<IppJobRef>> SendAsync(Job job, byte[]? jobData, CancellationToken ct)
  {
    if (job.Printer is null)
      return Error.Unexpected("Job.Unexpected", $"Printer can not have a value of null");

    if (jobData is null)
      return Error.NotFound("Job.MissingBytes", $"Job data can not be null");

    if (job.Printer.Host is null)
      return Error.Unexpected("Job.Unexpected", $"Host uri is null for printer {job.Printer.Name} ");

    using var stream = new MemoryStream(jobData);

    var request = new PrintJobRequest
    {
      OperationAttributes = new()
      {
        PrinterUri = new Uri($"ipp://{job.Printer.Host}:631/ipp/print"),
        JobName = job.FileName,
        DocumentFormat = job.ContentType,
      },

      Document = stream,
    };

    try
    {
      var response = await _client.PrintJobAsync(request);

      if (response.StatusCode == IppStatusCode.SuccessfulOk)
      {
        if (response.JobAttributes?.JobId != null)
          return new IppJobRef(job.PrinterId, job.Id, response.JobAttributes.JobId);
      }

      return Error.Failure("Printer.Failed", $"IPP status code: {response.StatusCode}, Message {response?.JobAttributes?.JobStateMessage}");
    }
    catch (SharpIpp.Exceptions.IppResponseException ex)
    {
      return Error.Failure(
          "Job.Failure",
          $"IPP error: {ex.Message} | Response:{ex.ResponseMessage?.StatusCode}"
      );
    }
    catch (Exception ex)
    {
      return Error.Failure("Job.Failure", $"Print job failed: {ex.Message}");
    }
  }

  public async Task<ErrorOr<List<IppJobStatus>>> GetPrinterJobsAsync(Printer printer, int[] ids)
  {

    List<IppJobStatus> ippJobs = [];

    var request = new GetJobsRequest
    {
      OperationAttributes = new()
      {
        PrinterUri = new Uri($"ipp://{printer.Host}:631/ipp/print"),
        JobIds = ids,
        RequestedAttributes = ["job-id", "job-uri", "job-state", "job-state-reasons", "job-state-message"]
      }
    };

    try
    {
      var response = await _client.GetJobsAsync(request);

      if (response.JobsAttributes != null)
      {
        ippJobs = [..response.JobsAttributes.Select(j => new IppJobStatus
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
      }
    }
    catch (SharpIpp.Exceptions.IppResponseException ex)
    {
      return Error.Failure("Jobs.Failure", $"IPP error: {ex.Message} | Response: {ex.ResponseMessage?.StatusCode}");
    }

    return ippJobs;
  }

  public async Task<ErrorOr<PrinterStatus>> GetPrinterStatusAsync(Printer printer)
  {
    var request = new GetPrinterAttributesRequest
    {
      OperationAttributes = new()
      {
        PrinterUri = new Uri($"ipp://{printer.Host}:631/ipp/print"),
        RequestedAttributes = ["printer-state", "printer-state-reasons", "printer-is-accepting-jobs"],
      }
    };

    try
    {
      var response = await _client.GetPrinterAttributesAsync(request);

      return response.PrinterAttributes?.PrinterState switch
      {
        PrinterState.Idle => PrinterStatus.Online,
        PrinterState.Processing => PrinterStatus.Online,
        PrinterState.Stopped => PrinterStatus.Offline,
        _ => PrinterStatus.Unknown,
      };
    }
    catch (SharpIpp.Exceptions.IppResponseException ex)
    {
      return Error.Failure("Printer.Status", $"IPP error: {ex.Message} | Response: {ex.ResponseMessage?.StatusCode}");
    }
    catch (Exception ex)
    {
      return Error.Failure("Printer.Status", $"Printer status check failed: {ex.Message}");
    }
  }
}


