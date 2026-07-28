using PrintSpooler.Core.Models;

namespace PrintSpooler.Core.Services;

public static class JobCancellationPolicy
{
    public static bool CanCancel(JobStatus status) => status == JobStatus.Queued;
}
