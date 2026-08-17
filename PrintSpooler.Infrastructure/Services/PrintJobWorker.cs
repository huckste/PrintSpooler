using System.Threading.Channels;
using ErrorOr;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PrintSpooler.Core.Models;
using PrintSpooler.Core.Services;
using PrintSpooler.Infrastructure.Data;

namespace PrintSpooler.Infrastructure.Services;

public class PrintJobWorker(
    IServiceScopeFactory scopeFactory,
    Channel<Guid> jobChannel,
    IPrinterDispatcher printerDispatcher,
    IJobNotifier jobNotifier
) : BackgroundService
{
  protected override async Task ExecuteAsync(CancellationToken ct)
  {
    await RequeuePendingJobs();

    await foreach (var jobId in jobChannel.Reader.ReadAllAsync(ct))
    {
      using var dataScope = scopeFactory.CreateScope();
      var dbContext = dataScope.ServiceProvider.GetRequiredService<AppDbContext>();

      var job = await dbContext
          .Jobs.Include(j => j.Printer)
          .FirstOrDefaultAsync(j => j.Id == jobId, ct);

      if (job is null)
        continue;

      if (job.Status == OperationState.Cancelled)
      {
        Audit(dbContext, job.Id, JobAction.Cancelled, ByWho.User);
        await RemoveJobData(dbContext, job.Id, ct);
      }
      else
        await HandleProcessing(dbContext, job, ct);

      await SaveAndUpdate(dbContext, job, ct);
    }
  }

  private async Task RequeuePendingJobs()
  {
    using var scope = scopeFactory.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    var pendingJobs = await dbContext
        .Jobs.Include(j => j.Printer)
        .Where(j => j.Status == OperationState.Queued || j.Status == OperationState.Submitting)
        .ToListAsync();

    foreach (var job in pendingJobs)
      await jobChannel.Writer.WriteAsync(job.Id);
  }

  private async Task HandleProcessing(AppDbContext dbContext, Job job, CancellationToken ct)
  {
    job.Status = OperationState.Submitting;

    var jobData = await dbContext.JobData.FirstOrDefaultAsync(d => d.JobId == job.Id, ct);

    await SaveAndUpdate(dbContext, job, ct);

    var result = await printerDispatcher.SendAsync(job, jobData?.Bytes, ct);

    if (result.IsError)
      HandleError(dbContext, job, result);
    else
      await MarkCompleted(dbContext, job, ct);
  }

  private static Task RemoveJobData(AppDbContext dbContext, Guid jobId, CancellationToken ct) =>
      dbContext.JobData.Where(d => d.JobId == jobId).ExecuteDeleteAsync(ct);

  private async Task MarkCompleted(AppDbContext dbContext, Job job, CancellationToken ct)
  {
    job.Status = OperationState.Completed;
    job.CompletedAt = DateTime.UtcNow;

    Audit(dbContext, job.Id, JobAction.Completed, ByWho.System);
    await RemoveJobData(dbContext, job.Id, ct);
  }

  private void HandleError(AppDbContext dbContext, Job job, ErrorOr<Success> result)
  {
    job.FailureReason = result.Errors.First().Description;
    job.Status = OperationState.Failed;
    Audit(dbContext, job.Id, JobAction.Failed, ByWho.System, job.FailureReason);
  }

  private void Audit(
      AppDbContext dbContext,
      Guid jobId,
      JobAction action,
      ByWho by,
      string? details = null
  ) =>
      dbContext.AuditLogs.Add(
          new AuditLog
          {
            Id = Guid.NewGuid(),
            JobId = jobId,
            Action = action,
            PerformedBy = by,
            Details = details,
          }
      );

  private async Task SaveAndUpdate(AppDbContext dbContext, Job job, CancellationToken ct)
  {
    await dbContext.SaveChangesAsync(ct);
    await jobNotifier.JobUpdateAsync(job, ct);
  }
}
