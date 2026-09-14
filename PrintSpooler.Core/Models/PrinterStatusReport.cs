namespace PrintSpooler.Core.Models;

public sealed record PrinterStatusReport(PrinterStatus Status, string? Reason = null, int? UpTimeSeconds = null);
