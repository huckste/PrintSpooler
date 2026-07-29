namespace PrintSpooler.Core.Models;

public class AuditLog
{
    public Guid Id { get; set; }
    public Guid JobId { get; set; }
    public required JobAction Action { get; set; }
    public required ByWho PerformedBy { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string? Details { get; set; }
}

public enum JobAction
{
    Created,
    Cancelled,
    Completed,
    Failed,
}

public enum ByWho
{
    System,
    User,
}
