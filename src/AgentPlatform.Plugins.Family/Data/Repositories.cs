using Microsoft.EntityFrameworkCore;

namespace AgentPlatform.Plugins.Family.Data;

public interface IPaymentsRepository
{
    Task<Guid> CreateAsync(Payment payment, CancellationToken ct);
    Task<Payment?> FindPendingByCreditorAsync(Guid groupId, string creditor, CancellationToken ct);
    Task<Payment?> GetAsync(Guid id, CancellationToken ct);
    Task<bool> MarkPaidAsync(Guid id, Guid userId, string idempotencyKey, CancellationToken ct);
    Task<IReadOnlyList<Payment>> ListDueAsync(Guid groupId, CancellationToken ct);
    Task<IReadOnlyList<Payment>> ListAllAsync(Guid groupId, CancellationToken ct);
}

public sealed class PaymentsRepository(FamilyDbContext db) : IPaymentsRepository
{
    public async Task<Guid> CreateAsync(Payment payment, CancellationToken ct)
    {
        db.Payments.Add(payment);
        await db.SaveChangesAsync(ct);
        return payment.Id;
    }

    public Task<Payment?> FindPendingByCreditorAsync(Guid groupId, string creditor, CancellationToken ct) =>
        db.Payments.Where(p => p.GroupId == groupId && p.Status == "pending"
                && EF.Functions.ILike(p.Creditor, $"%{creditor}%"))
            .OrderBy(p => p.DueDate)
            .FirstOrDefaultAsync(ct);

    public Task<Payment?> GetAsync(Guid id, CancellationToken ct) =>
        db.Payments.FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<bool> MarkPaidAsync(Guid id, Guid userId, string idempotencyKey, CancellationToken ct)
    {
        var affected = await db.Payments
            .Where(p => p.Id == id && p.Status == "pending")
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.Status, "paid")
                .SetProperty(p => p.PaidAt, DateTimeOffset.UtcNow)
                .SetProperty(p => p.PaidBy, userId)
                .SetProperty(p => p.IdempotencyKey, idempotencyKey), ct);
        return affected > 0;
    }

    public async Task<IReadOnlyList<Payment>> ListDueAsync(Guid groupId, CancellationToken ct) =>
        await db.Payments.AsNoTracking()
            .Where(p => p.GroupId == groupId && p.Status == "pending")
            .OrderBy(p => p.DueDate)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Payment>> ListAllAsync(Guid groupId, CancellationToken ct) =>
        await db.Payments.AsNoTracking()
            .Where(p => p.GroupId == groupId)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(ct);
}

public interface ITasksRepository
{
    Task<Guid> CreateAsync(TaskItem task, CancellationToken ct);
    Task<bool> MarkDoneAsync(Guid id, Guid userId, CancellationToken ct);
    Task<IReadOnlyList<TaskItem>> ListOpenAsync(Guid groupId, CancellationToken ct);
}

public sealed class TasksRepository(FamilyDbContext db) : ITasksRepository
{
    public async Task<Guid> CreateAsync(TaskItem task, CancellationToken ct)
    {
        db.Tasks.Add(task);
        await db.SaveChangesAsync(ct);
        return task.Id;
    }

    public async Task<bool> MarkDoneAsync(Guid id, Guid userId, CancellationToken ct)
    {
        var affected = await db.Tasks.Where(t => t.Id == id && t.Status == "pending")
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.Status, "done")
                .SetProperty(t => t.DoneAt, DateTimeOffset.UtcNow)
                .SetProperty(t => t.DoneBy, userId), ct);
        return affected > 0;
    }

    public async Task<IReadOnlyList<TaskItem>> ListOpenAsync(Guid groupId, CancellationToken ct) =>
        await db.Tasks.AsNoTracking()
            .Where(t => t.GroupId == groupId && t.Status == "pending")
            .OrderBy(t => t.DueDate)
            .ToListAsync(ct);
}
