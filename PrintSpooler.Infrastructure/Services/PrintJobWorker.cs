using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PrintSpooler.Core.Models;
using PrintSpooler.Core.Services;
using PrintSpooler.Infrastructure.Data;

namespace PrintSpooler.Infrastructure.Services;

public class PrintJobWorker(
    IServiceScopeFactory scopeFactory,
    Channel<Job> jobChannel,
    IPrinterDispatcher printerDispatcher
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var queuedJobs = await dbContext
            .Jobs.Include(j => j.Printer)
            .Where(j => j.Status == JobStatus.Queued)
            .ToListAsync();

        foreach (var job in queuedJobs)
            await jobChannel.Writer.WriteAsync(job);

        await foreach (var job in jobChannel.Reader.ReadAllAsync(ct))
        {
            var result = await printerDispatcher.SendAsync(job, ct);

            using var resultScope = scopeFactory.CreateScope();
            var resultDbContext = resultScope.ServiceProvider.GetRequiredService<AppDbContext>();

            var dbJob = await resultDbContext.Jobs.FirstOrDefaultAsync(j => j.Id == job.Id, ct);

            if (dbJob is not null)
            {
                if (result.IsError)
                {
                    if (dbJob.RetryCount >= dbJob.MaxRetries)
                    {
                        dbJob.Status = JobStatus.Failed;
                        dbJob.FailureReason = result.Errors.First().Description;

                        resultDbContext.AuditLogs.Add(
                            new AuditLog
                            {
                                Id = Guid.NewGuid(),
                                JobId = job.Id,
                                Action = JobAction.Failed,
                                PerformedBy = ByWho.System,
                                Details = dbJob.FailureReason,
                            }
                        );
                    }
                    else
                    {
                        dbJob.Status = JobStatus.Queued;
                        dbJob.RetryCount++;
                        await jobChannel.Writer.WriteAsync(job);
                    }
                }
                else
                {
                    dbJob.Status = JobStatus.Completed;

                    resultDbContext.AuditLogs.Add(
                        new AuditLog
                        {
                            Id = Guid.NewGuid(),
                            JobId = job.Id,
                            Action = JobAction.Completed,
                            PerformedBy = ByWho.System,
                        }
                    );

                    dbJob.CompletedAt = DateTime.UtcNow;
                }

                await resultDbContext.SaveChangesAsync(ct);
            }
        }
    }
}
