namespace PrintSpooler.Infrastructure.Services;

using System.Threading.Channels;
using ErrorOr;
using Microsoft.EntityFrameworkCore;
using PrintSpooler.Core.Models;
using PrintSpooler.Core.Services;
using PrintSpooler.Infrastructure.Data;

public class JobService(
    AppDbContext dbContext,
    Channel<Guid> jobChannel,
    IJobNotifier jobNotifier,
    IPrinterDispatcher printerDispatcher
    ) : IJobService
{
  public async Task<ErrorOr<Job>> CreateJob(JobCreationData data)
  {
    Printer? printerRes = await dbContext.Printers
      .FirstOrDefaultAsync(p => p.Id == data.PrinterId);

    if (printerRes is not { } printer)
      return Error.NotFound("Printer.NotFound", $"No printer found with ID {data.PrinterId}");

    var job = new Job
    {
      Id = Guid.NewGuid(),
      SubmittedBy = data.SubmittedBy,
      FileName = data.FileName,
      ContentType = data.ContentType,
      FileSizeBytes = data.Bytes.Length,
      PrinterId = data.PrinterId,
      Printer = printer,
      Status = JobPolicies.DefaultStatus
    };

    dbContext.Jobs.Add(job);
    dbContext.JobData.Add(new JobData { Bytes = data.Bytes, JobId = job.Id });
    await dbContext.SaveChangesAsync();

    var result = await UpdateJob(
        new JobUpdate(job.Id, job.Status)
          .Log(JobAction.Created, ByWho.User)
          .WriteToChannel()
      );

    if (result.IsError)
      return result.Errors;

    return job;
  }

  public async Task<ErrorOr<Job>> GetJob(Guid id)
  {
    Job? result = await dbContext.Jobs.Include(j => j.Printer).FirstOrDefaultAsync(j => j.Id == id);

    if (result is not { } job)
      return Error.NotFound("Job.NotFound", $"No job found with ID {id}");

    return job;
  }

  public async Task<List<Job>> GetAllActiveJobs() =>
      await dbContext
          .Jobs.Include(j => j.Printer)
          .Where(j => JobPolicies.Active.Contains(j.Status))
          .ToListAsync();

  public async Task<ErrorOr<JobData>> GetJobData(Guid jobId, CancellationToken ct = default)
  {
    JobData? result = await dbContext.JobData.FirstOrDefaultAsync(d => d.JobId == jobId, ct);

    if (result is not { } jobData)
      return Error.NotFound("JobData.NotFound", $"No job data found for job ID {jobId}");

    return jobData;
  }

  public async Task<List<Job>> GetInFlightJobs() =>
    await dbContext
      .Jobs.Include(j => j.Printer)
      .Where(j => JobPolicies.InFlight.Contains(j.Status))
      .ToListAsync();

  public async Task<List<Job>> GetPendingJobs() =>
    await dbContext
      .Jobs.Include(j => j.Printer)
      .Where(j => JobPolicies.Pending.Contains(j.Status))
      .ToListAsync();

  public async Task<ErrorOr<Job>> CancelJob(Guid id, CancellationToken ct = default)
  {
    var job = await GetJob(id).Then(JobPolicies.CanCancel);

    if (job.IsError)
      return job.Errors;

    // null IPP id means job never reached printer. Nothing to cancel
    if (job.Value.IppJobId is not { } ippJobId)
    {
      var cancelled = await UpdateJob(new JobUpdate(id, JobStatus.Cancelled)
        .Log(JobAction.Cancelled, ByWho.User)
        .NotifyDashboard(), ct);

      return cancelled.IsError ? cancelled.Errors : job;
    }

    var result = await printerDispatcher.CancelPrinterJob(job.Value.Printer, ippJobId, ct);

    if (result.IsError)
    {
      var failed = new JobUpdate(id, job.Value.Status).NotifyDashboard();
      failed.FailureReason = result.Errors.First().Description;

      var failedUpdate = await UpdateJob(failed, ct);

      return failedUpdate.IsError ? failedUpdate.Errors : result.Errors;
    }

    // The printer accepted the cancel.
    // PrinterWatch reports the job cancellation.
    var cancelling = await UpdateJob(new JobUpdate(id, JobStatus.Cancelling)
      .Log(JobAction.CancelRequested, ByWho.User)
      .NotifyDashboard(), ct);

    return cancelling.IsError ? cancelling.Errors : job;
  }

  public async Task<ErrorOr<Job>> RetryJob(Guid id)
  {
    var job = await GetJob(id);

    var update = await job
      .Then(JobPolicies.CanRetry)
      .ThenAsync(j =>
        UpdateJob(
          new JobUpdate(j.Id, JobStatus.Queued)
          .RetryJob()
          .Log(JobAction.Retried, ByWho.User)
          .NotifyDashboard()
          .WriteToChannel()
        )
      );

    return update.IsError ? update.Errors : job;
  }

  // ExecuteUpdateAsync bypasses the change tracker,
  // which is fine because the worker's scope ends right after the call.
  public async Task<ErrorOr<Success>> SetIppJobId(Guid jobId, int ippJobId, CancellationToken ct = default)
  {
    int rowsUpdated = await dbContext.Jobs
      .Where(j => j.Id == jobId)
      .ExecuteUpdateAsync(s => s.SetProperty(j => j.IppJobId, ippJobId), ct);

    return rowsUpdated == 1
      ? Result.Success
      : Error.Failure("Job.SetIppJobId", $"Expected to update one job for id {jobId}, updated {rowsUpdated}");
  }

  public async Task<ErrorOr<Success>> RemoveJobData(Guid jobId, CancellationToken ct = default)
  {
    int rowsDeleted = await dbContext.JobData
      .Where(d => d.JobId == jobId)
      .ExecuteDeleteAsync(ct);

    Error? error = rowsDeleted switch
    {
      < 1 => Error.Failure("Job.RemoveJobData", $"Failed to delete job data for id: {jobId}"),
      > 1 => Error.Unexpected("Job.RemoveJobData", $"Deleted more than one row: {rowsDeleted}"),
      _ => null
    };

    return error == null ? Result.Success : (Error)error;
  }

  public async Task<ErrorOr<Success>> UpdateJob(JobUpdate update, CancellationToken ct = default)
  {
    if (update.AuditLog != null)
      dbContext.AuditLogs.Add(update.AuditLog);

    ErrorOr<Job> job = await GetJob(update.JobId)
      .Then(j =>
      {
        j.Status = update.Status;
        j.FailureReason = update.FailureReason;

        if (update.Retry)
          j.RetryCount++;

        return j;
      });

    if (job.IsError)
      return job.Errors;

    if (JobPolicies.IsTerminal(job.Value.Status))
    {
      var rowsDeleted = await RemoveJobData(job.Value.Id);

      if (rowsDeleted.IsError)
        return rowsDeleted.Errors;
    }

    await dbContext.SaveChangesAsync(ct);

    if (update.Notify)
      await jobNotifier.JobUpdateAsync(job.Value, ct);

    if (update.Write)
      await jobChannel.Writer.WriteAsync(job.Value.Id);

    return Result.Success;
  }
}
