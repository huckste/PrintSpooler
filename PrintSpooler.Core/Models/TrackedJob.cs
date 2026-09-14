namespace PrintSpooler.Core.Models;

public sealed record TrackedJob(Guid Id, TaskCompletionSource<JobStatus> CompletionSource, int MissedPolls = 0);
