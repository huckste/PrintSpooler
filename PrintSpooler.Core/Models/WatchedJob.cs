namespace PrintSpooler.Core.Models;

/// <summary>
/// PrinterWatch's bookkeeping for one in-flight IPP job. Deliberately holds no
/// status: the DB is the only record of what a job's status is, and a second
/// copy here could only ever drift from it. MissedPolls is the opposite — it
/// counts consecutive polls in which the printer said nothing about this job,
/// which is knowledge the DB cannot have.
/// </summary>
public sealed record WatchedJob(Guid JobId, int MissedPolls = 0);
