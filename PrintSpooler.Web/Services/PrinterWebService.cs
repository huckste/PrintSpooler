using PrintSpooler.Core.Models;

namespace PrintSpooler.Web.Services;

public static class PrinterWebService
{
  public static bool IsOnline(PrinterStatus status) => status is PrinterStatus.Idle or PrinterStatus.Processing or PrinterStatus.Stopped;

  public static string DotClass(PrinterStatus status) => status switch
  {
    PrinterStatus.Idle or PrinterStatus.Processing => "online",
    PrinterStatus.Stopped => "stopped",
    _ => "offline", // Offline, Unknown, or anything we can't positively classify
  };
  public static int PrintersOnlineCount(List<Printer> printers) => printers.Count(p => IsOnline(p.Status));
  public static int PrintersOfflineCount(List<Printer> printers) => printers.Count(p => !IsOnline(p.Status));

  public static string MessageSeverityClass(string reason) =>
    reason.Contains("-error", StringComparison.OrdinalIgnoreCase) ? "error" : "warning";
}
