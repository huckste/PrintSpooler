using PrintSpooler.Core.Models;

namespace PrintSpooler.Core.Services;

public static class JobCancellationPolicy
{
  public static bool CanCancel(OperationState status) =>
      status == OperationState.Queued || status == OperationState.Failed;
}
