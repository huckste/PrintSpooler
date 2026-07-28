namespace PrintSpooler.Core.Models;

public class AuditLog
{
    public Guid Id { get; set; }
    public Guid JobId { get; set; }
    public required string Action { get; set; }
    public required string PerformedBy { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string? Details { get; set; }
}
