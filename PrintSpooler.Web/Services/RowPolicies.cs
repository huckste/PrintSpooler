using PrintSpooler.Core.Models;
using PrintSpooler.Core.Services;
using PrintSpooler.Web.Models;

namespace PrintSpooler.Web.Services;

// Anything with a real Job defers to Core's JobPolicies
// Staged is a web only concept (null jobId)
public static class RowPolicies
{
  public static bool CanSend(QueueRow row) => row.IsStaged;

  public static bool CanRetry(QueueRow row) =>
    row.Status is { } status && JobPolicies.Retryable.Contains(status);

  // Mirrors Core's JobPolicies.CanCancel
  public static bool CanCancel(QueueRow row) =>
    row.IsStaged || (row.Status is { } status
      && JobPolicies.Cancellable.Contains(status)
      && !(status is JobStatus.Submitting && row.IppJobId is null));
}
