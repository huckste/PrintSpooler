using PrintSpooler.Core.Services;
using PrintSpooler.Web.Models;

namespace PrintSpooler.Web.Services;

// Row actions the dashboard is allowed to offer. Staged rows are a Web-only
// concept, so they are decided here; anything with a real Job defers to Core's
// JobPolicies so the button set can never drift from what the API will accept.
public static class RowPolicies
{
  public static bool CanSend(QueueRow row) => row.IsStaged;

  public static bool CanRetry(QueueRow row) =>
    row.Status is { } status && JobPolicies.Retryable.Contains(status);

  public static bool CanCancel(QueueRow row) =>
    row.IsStaged || (row.Status is { } status && JobPolicies.IsActive(status));
}
