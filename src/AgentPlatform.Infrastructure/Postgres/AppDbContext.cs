using AgentPlatform.Infrastructure.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace AgentPlatform.Infrastructure.Postgres;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<UserToken> UserTokens => Set<UserToken>();
    public DbSet<Group> Groups => Set<Group>();
    public DbSet<GroupMember> GroupMembers => Set<GroupMember>();
    public DbSet<IdempotencyKey> IdempotencyKeys => Set<IdempotencyKey>();
    public DbSet<PendingIntentEntity> PendingIntents => Set<PendingIntentEntity>();
    public DbSet<AuditLogEntity> AuditLog => Set<AuditLogEntity>();
    public DbSet<UsageMeterEventEntity> UsageMeterEvents => Set<UsageMeterEventEntity>();
    public DbSet<BudgetStateEntity> BudgetStates => Set<BudgetStateEntity>();
    public DbSet<SchedulerJobEntity> SchedulerJobs => Set<SchedulerJobEntity>();
    public DbSet<PendingConfirmationEntity> PendingConfirmations => Set<PendingConfirmationEntity>();
    public DbSet<DeadLetterEntity> DeadLetterQueue => Set<DeadLetterEntity>();
    public DbSet<PromptVersionEntity> PromptVersions => Set<PromptVersionEntity>();
    public DbSet<ConversationEntity> Conversations => Set<ConversationEntity>();
    public DbSet<ConversationMessageEntity> ConversationMessages => Set<ConversationMessageEntity>();
    public DbSet<MemoryFactEntity> MemoryFacts => Set<MemoryFactEntity>();
    public DbSet<ChannelIdentity> ChannelIdentities => Set<ChannelIdentity>();
    public DbSet<AutomationRuleEntity> AutomationRules => Set<AutomationRuleEntity>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>().HasKey(x => x.Id);

        // Generic messaging-channel bindings (replaces per-channel columns on users; email lives here too).
        b.Entity<ChannelIdentity>().HasKey(x => x.Id);
        b.Entity<ChannelIdentity>().HasIndex(x => new { x.ChannelId, x.ExternalId }).IsUnique();
        b.Entity<ChannelIdentity>().HasIndex(x => new { x.UserId, x.ChannelId });

        b.Entity<UserToken>().HasKey(x => x.Id);
        b.Entity<UserToken>().HasIndex(x => x.TokenHash).IsUnique();
        b.Entity<UserToken>().HasIndex(x => x.UserId);

        b.Entity<Group>().HasKey(x => x.Id);
        b.Entity<GroupMember>().HasKey(x => new { x.GroupId, x.UserId });

        b.Entity<IdempotencyKey>().HasKey(x => x.Key);
        b.Entity<IdempotencyKey>().Property(x => x.Result).HasColumnType("jsonb");

        b.Entity<PendingIntentEntity>().HasKey(x => x.Id);
        b.Entity<PendingIntentEntity>().Property(x => x.GatheredSlots).HasColumnType("jsonb");

        b.Entity<AuditLogEntity>().HasKey(x => x.Id);
        b.Entity<AuditLogEntity>().Property(x => x.Metadata).HasColumnType("jsonb");
        b.Entity<AuditLogEntity>().HasIndex(x => new { x.GroupId, x.OccurredAt });
        b.Entity<AuditLogEntity>().HasIndex(x => new { x.UserId, x.OccurredAt });

        b.Entity<UsageMeterEventEntity>().HasKey(x => x.Id);
        b.Entity<UsageMeterEventEntity>().HasIndex(x => new { x.GroupId, x.OccurredAt });

        b.Entity<BudgetStateEntity>().HasKey(x => x.ScopeKey);

        b.Entity<SchedulerJobEntity>().HasKey(x => x.Id);
        b.Entity<SchedulerJobEntity>().Property(x => x.Payload).HasColumnType("jsonb");
        b.Entity<SchedulerJobEntity>().HasIndex(x => x.NextRunAt);

        b.Entity<PendingConfirmationEntity>().HasKey(x => x.Id);
        b.Entity<PendingConfirmationEntity>().Property(x => x.ActionPlan).HasColumnType("jsonb");
        b.Entity<PendingConfirmationEntity>().HasIndex(x => new { x.UserId, x.ExpiresAt });

        b.Entity<DeadLetterEntity>().HasKey(x => x.Id);
        b.Entity<DeadLetterEntity>().Property(x => x.Input).HasColumnType("jsonb");

        b.Entity<PromptVersionEntity>().HasKey(x => x.Id);

        b.Entity<ConversationEntity>().HasKey(x => x.Id);
        b.Entity<ConversationEntity>().HasIndex(x => new { x.UserId, x.Status });

        b.Entity<ConversationMessageEntity>().HasKey(x => x.Id);
        b.Entity<ConversationMessageEntity>().HasIndex(x => new { x.ConversationId, x.CreatedAt });

        b.Entity<MemoryFactEntity>().HasKey(x => x.Id);
        b.Entity<MemoryFactEntity>().HasIndex(x => new { x.GroupId, x.UserId, x.Key }).IsUnique();

        b.Entity<AutomationRuleEntity>().HasKey(x => x.Id);
        b.Entity<AutomationRuleEntity>().HasIndex(x => new { x.Enabled, x.NextRunAt });
        b.Entity<AutomationRuleEntity>().HasIndex(x => x.UserId);
    }
}
