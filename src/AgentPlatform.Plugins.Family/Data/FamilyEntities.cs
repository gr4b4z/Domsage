using System.ComponentModel.DataAnnotations.Schema;

namespace AgentPlatform.Plugins.Family.Data;

[Table("payments")]
public class Payment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid GroupId { get; set; }
    public string Creditor { get; set; } = "";
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "PLN";
    public DateOnly DueDate { get; set; }
    public string Status { get; set; } = "pending";
    public DateTimeOffset? PaidAt { get; set; }
    public Guid? PaidBy { get; set; }
    public string? IdempotencyKey { get; set; }
    public string Source { get; set; } = "manual";
    public Guid? InvoiceDocId { get; set; }
    public decimal? Confidence { get; set; }
    public string? ExtractedRaw { get; set; }
    public string? Notes { get; set; }
    public int LeadDays { get; set; } = 3;                  // remind this many days before due
    public DateTimeOffset? RemindedAt { get; set; }         // first reminder sent
    public DateTimeOffset? EscalatedAt { get; set; }        // escalation (re-broadcast) sent
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

[Table("tasks")]
public class TaskItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid GroupId { get; set; }
    public Guid? AssignedTo { get; set; }
    public string Title { get; set; } = "";
    public DateOnly? DueDate { get; set; }
    public string Status { get; set; } = "pending";
    public DateTimeOffset? DoneAt { get; set; }
    public Guid? DoneBy { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
