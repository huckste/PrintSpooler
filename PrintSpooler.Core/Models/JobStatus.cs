namespace PrintSpooler.Core.Models;

public enum JobStatus
{
  Queued,

  // Handed off to the printer but not yet printing. Covers both our own
  // send-in-progress window and the printer's IPP pending/pending-held states —
  // in all of them the bytes are gone from us and nothing has hit paper yet.
  Submitting,

  Processing,
  Completed,
  Cancelled,
  Failed
}
