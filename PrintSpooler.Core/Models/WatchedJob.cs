namespace PrintSpooler.Core.Models;

public sealed record WatchedJob(Guid JobId, JobStatus State);
