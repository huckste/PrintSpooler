namespace PrintSpooler.Core.Models;

public record PrinterEvent(Guid PrinterId, PrinterEventType EventType);
