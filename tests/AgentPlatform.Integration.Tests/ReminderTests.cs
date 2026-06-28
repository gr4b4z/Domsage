using AgentPlatform.Core.Contracts;
using AgentPlatform.Core.DI;
using AgentPlatform.Infrastructure.Postgres;
using AgentPlatform.Infrastructure.Postgres.Entities;
using AgentPlatform.Plugins.Family.Data;
using AgentPlatform.Plugins.Family.Reminders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AgentPlatform.Integration.Tests;

[Collection("postgres")]
public class ReminderTests(PostgresFixture fx)
{
    private FamilyDbContext Db() => new(new DbContextOptionsBuilder<FamilyDbContext>()
        .UseNpgsql(fx.ConnectionString).UseSnakeCaseNamingConvention().Options);

    private AppDbContext CoreDb() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql(fx.ConnectionString).UseSnakeCaseNamingConvention().Options);

    [Fact]
    public async Task Payment_Reminder_Then_Escalation()
    {
        await using var db = Db();
        var repo = new PaymentsRepository(db);
        var g = Guid.NewGuid();
        var ct = CancellationToken.None;

        // Due in 2 days, lead 3 → within reminder window. Not-due (in 30 days) → not yet.
        await repo.CreateAsync(new Payment { GroupId = g, Creditor = "Gaz", Amount = 100, Currency = "PLN",
            DueDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(2), LeadDays = 3, Status = "pending" }, ct);
        await repo.CreateAsync(new Payment { GroupId = g, Creditor = "Later", Amount = 50, Currency = "PLN",
            DueDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30), LeadDays = 3, Status = "pending" }, ct);

        var due = await repo.DueForReminderAsync(g, ct);
        Assert.Single(due);
        Assert.Equal("Gaz", due[0].Creditor);

        // Before reminding, nothing to escalate.
        Assert.Empty(await repo.DueForEscalationAsync(g, TimeSpan.Zero, ct));

        await repo.MarkRemindedAsync(due[0].Id, ct);
        // Not reminded again (reminded_at set).
        Assert.Empty(await repo.DueForReminderAsync(g, ct));

        // Escalation window 0 → immediately eligible since reminded_at is in the past.
        var esc = await repo.DueForEscalationAsync(g, TimeSpan.Zero, ct);
        Assert.Single(esc);
        await repo.MarkEscalatedAsync(esc[0].Id, ct);
        Assert.Empty(await repo.DueForEscalationAsync(g, TimeSpan.Zero, ct)); // not twice

        // Paying it removes it from any future scan.
        await repo.MarkPaidAsync(due[0].Id, Guid.NewGuid(), "k", ct);
        Assert.Empty(await repo.DueForReminderAsync(g, ct));
    }

    [Fact]
    public async Task Renewal_Reminder_Then_Escalation()
    {
        await using var db = Db();
        var repo = new RenewalsRepository(db);
        var g = Guid.NewGuid();
        var ct = CancellationToken.None;

        await repo.AddAsync(new Renewal { GroupId = g, Category = "car", Label = "OC",
            ExpiresOn = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(10), LeadDays = 30, EscalateDays = 7, Status = "active" }, ct);

        var due = await repo.DueForReminderAsync(g, ct);
        Assert.Single(due);
        await repo.MarkRemindedAsync(due[0].Id, ct);
        Assert.Empty(await repo.DueForReminderAsync(g, ct));

        // EscalateDays=7 and reminded just now → not yet eligible.
        Assert.Empty(await repo.DueForEscalationAsync(g, ct));
    }

    [Fact]
    public async Task MarkRenewed_Is_FirstWins_And_Stops_Scans()
    {
        await using var db = Db();
        var repo = new RenewalsRepository(db);
        var g = Guid.NewGuid();
        var ct = CancellationToken.None;
        var id = await repo.AddAsync(new Renewal { GroupId = g, Category = "car", Label = "OC",
            ExpiresOn = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(10), LeadDays = 30, Status = "active" }, ct);

        Assert.True(await repo.MarkRenewedAsync(id, ct));   // first wins
        Assert.False(await repo.MarkRenewedAsync(id, ct));  // already renewed
        Assert.Empty(await repo.DueForReminderAsync(g, ct)); // no longer active → no nagging
    }

    // The unification proof: a scan-based payment reminder carries the generic ack-action
    // (toolId + input) so the user can tap "✅ Zapłacone" — core dispatches it by id, blind to domain.
    [Fact]
    public async Task Scanner_Attaches_Ack_Action_To_Payment_Reminder()
    {
        await using var core = CoreDb();
        await using var fam = Db();
        var ct = CancellationToken.None;

        var g = Guid.NewGuid();
        core.Groups.Add(new Group { Id = g, Type = "household", Name = "Test" });
        await core.SaveChangesAsync(ct);

        var payRepo = new PaymentsRepository(fam);
        var payId = await payRepo.CreateAsync(new Payment { GroupId = g, Creditor = "Prąd", Amount = 200,
            Currency = "PLN", DueDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1), LeadDays = 3,
            Status = "pending" }, ct);

        var notifier = new CapturingNotifier();
        var scanner = new ReminderScanner(core, payRepo, new RenewalsRepository(fam),
            new ExecutionContextAccessor(), new FakeDir(Guid.NewGuid().ToString()), notifier,
            new NoopAudit(), Options.Create(new ReminderOptions()), NullLogger<ReminderScanner>.Instance);

        await scanner.ScanAsync(ct);

        var ev = Assert.Single(notifier.Events.Where(
            e => e.ActionInput is not null && e.ActionInput.Contains(payId.ToString())));
        Assert.Equal("payment.reminder", ev.Type);
        Assert.Equal("family.payments.mark_paid", ev.ActionToolId);
        Assert.Equal("✅ Zapłacone", ev.ActionLabel);
    }

    [Fact]
    public async Task ChannelIdentity_Links_And_Moves_To_Newest_User()
    {
        await using var core = CoreDb();
        var ct = CancellationToken.None;
        var repo = new AgentPlatform.Infrastructure.Repositories.UserRepository(core);

        var g = Guid.NewGuid();
        core.Groups.Add(new Group { Id = g, Type = "household", Name = "Dom" });
        var u1 = new User { DisplayName = "Aga" };
        var u2 = new User { DisplayName = "Ola" };
        core.Users.AddRange(u1, u2);
        core.GroupMembers.AddRange(
            new AgentPlatform.Infrastructure.Postgres.Entities.GroupMember { GroupId = g, UserId = u1.Id, Role = "member" },
            new AgentPlatform.Infrastructure.Postgres.Entities.GroupMember { GroupId = g, UserId = u2.Id, Role = "member" });
        await core.SaveChangesAsync(ct);

        const string chat = "555111222";
        Assert.True(await repo.SetChannelIdentityAsync(u1.Id.ToString(), "telegram", chat, ct));
        var found = await repo.GetByChannelIdentityAsync("telegram", chat, ct);
        Assert.Equal(u1.Id.ToString(), found!.UserId);

        // Re-linking the same chat to u2 moves it (one external id maps to exactly one user).
        Assert.True(await repo.SetChannelIdentityAsync(u2.Id.ToString(), "telegram", chat, ct));
        found = await repo.GetByChannelIdentityAsync("telegram", chat, ct);
        Assert.Equal(u2.Id.ToString(), found!.UserId);

        // Unknown user → no binding.
        Assert.False(await repo.SetChannelIdentityAsync(Guid.NewGuid().ToString(), "telegram", "999", ct));
    }

    [Fact]
    public async Task EmailIdentities_Multiple_ResolveToUser_FirstIsPrimary()
    {
        await using var core = CoreDb();
        var ct = CancellationToken.None;
        var repo = new AgentPlatform.Infrastructure.Repositories.UserRepository(core);

        var g = Guid.NewGuid();
        core.Groups.Add(new Group { Id = g, Type = "household", Name = "Dom" });
        var u = new User { DisplayName = "Jan" };
        core.Users.Add(u);
        core.GroupMembers.Add(new AgentPlatform.Infrastructure.Postgres.Entities.GroupMember
        { GroupId = g, UserId = u.Id, Role = "member" });
        await core.SaveChangesAsync(ct);

        // First email becomes primary; a second is additional. Both resolve (case-insensitive).
        Assert.True(await repo.AddEmailIdentityAsync(u.Id.ToString(), "Jan@Home.PL", false, ct));
        Assert.True(await repo.AddEmailIdentityAsync(u.Id.ToString(), "jan@work.com", false, ct));
        Assert.Equal(u.Id.ToString(), (await repo.GetByChannelIdentityAsync("email", "jan@home.pl", ct))!.UserId);
        Assert.Equal(u.Id.ToString(), (await repo.GetByChannelIdentityAsync("email", "jan@work.com", ct))!.UserId);

        var primary = await core.ChannelIdentities.AsNoTracking()
            .Where(c => c.UserId == u.Id && c.ChannelId == "email" && c.IsPrimary)
            .Select(c => c.ExternalId).ToListAsync(ct);
        Assert.Equal(["jan@home.pl"], primary); // exactly one, the first

        // Promoting the second moves the primary flag.
        Assert.True(await repo.AddEmailIdentityAsync(u.Id.ToString(), "jan@work.com", true, ct));
        var promoted = await core.ChannelIdentities.AsNoTracking()
            .Where(c => c.UserId == u.Id && c.ChannelId == "email" && c.IsPrimary)
            .Select(c => c.ExternalId).ToListAsync(ct);
        Assert.Equal(["jan@work.com"], promoted);
    }

    private sealed class CapturingNotifier : INotificationService
    {
        public readonly List<LiveEvent> Events = [];
        public Task NotifyUsersAsync(IEnumerable<string> userIds, LiveEvent evt, CancellationToken ct)
        { Events.Add(evt); return Task.CompletedTask; }
    }

    private sealed class FakeDir(string memberId) : IGroupDirectory
    {
        public Task<IReadOnlyList<AgentPlatform.Core.Contracts.GroupMember>> GetMembersAsync(string groupId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<AgentPlatform.Core.Contracts.GroupMember>>(
                [new AgentPlatform.Core.Contracts.GroupMember(memberId, "X", null)]);
        public Task<IReadOnlyList<AgentPlatform.Core.Contracts.GroupMember>> ResolveByNamesAsync(string groupId, IEnumerable<string> names, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<AgentPlatform.Core.Contracts.GroupMember>>([]);
    }

    private sealed class NoopAudit : IAuditLogRepository
    {
        public Task WriteAsync(AuditEntry entry, CancellationToken ct) => Task.CompletedTask;
        public Task<IReadOnlyList<AuditActionRecord>> SearchActionsAsync(string? groupId, string query, int limit, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<AuditActionRecord>>([]);
    }
}
