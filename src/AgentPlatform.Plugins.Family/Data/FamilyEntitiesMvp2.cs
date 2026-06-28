using System.ComponentModel.DataAnnotations.Schema;

namespace AgentPlatform.Plugins.Family.Data;

// One shared household shopping list (no per-store lists — matches how real homes work).
[Table("shopping_items")]
public class ShoppingItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid GroupId { get; set; }
    public string Name { get; set; } = "";
    public string? Quantity { get; set; }
    public string Status { get; set; } = "needed";
    public Guid AddedBy { get; set; }
    public Guid? BoughtBy { get; set; }
    public DateTimeOffset? BoughtAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

// Who currently receives shopping notifications. Default: nobody.
// Standing opt-in (Until = null) or per-trip (Until = now + TTL).
[Table("shopping_watchers")]
public class ShoppingWatcher
{
    public Guid GroupId { get; set; }
    public Guid UserId { get; set; }
    public DateTimeOffset? Until { get; set; }  // null = standing
}

[Table("renewals")]
public class Renewal
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid GroupId { get; set; }
    public string Category { get; set; } = "";
    public string Label { get; set; } = "";
    public DateOnly ExpiresOn { get; set; }
    public int LeadDays { get; set; } = 30;
    public int EscalateDays { get; set; } = 7;
    public string Status { get; set; } = "active";
    public DateTimeOffset? LastRemindedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

[Table("chores")]
public class Chore
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid GroupId { get; set; }
    public Guid AssignedTo { get; set; }
    public string Title { get; set; } = "";
    public string? RRule { get; set; }
    public decimal? AllowanceAmount { get; set; }
    public string AllowanceCurrency { get; set; } = "PLN";
    public string Status { get; set; } = "pending";
    public DateTimeOffset? CompletedAt { get; set; }
    public Guid? ConfirmedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
