using AgentPlatform.Infrastructure.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AgentPlatform.Plugins.Family.Data;

/// <summary>Family plugin DbContext — owns the 'family' schema with its own migrations + RLS.</summary>
public sealed class FamilyDbContext : DbContext
{
    private readonly RlsConnectionInterceptor? _rls;

    public FamilyDbContext(DbContextOptions<FamilyDbContext> options, RlsConnectionInterceptor rls)
        : base(options) => _rls = rls;

    public FamilyDbContext(DbContextOptions<FamilyDbContext> options) : base(options) { }

    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<TaskItem> Tasks => Set<TaskItem>();
    public DbSet<ShoppingItem> ShoppingItems => Set<ShoppingItem>();
    public DbSet<ShoppingWatcher> ShoppingWatchers => Set<ShoppingWatcher>();
    public DbSet<Renewal> Renewals => Set<Renewal>();
    public DbSet<Chore> Chores => Set<Chore>();
    public DbSet<InvoiceDocument> InvoiceDocuments => Set<InvoiceDocument>();

    protected override void OnConfiguring(DbContextOptionsBuilder o)
    {
        if (_rls is not null) o.AddInterceptors(_rls);
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("family");
        b.Entity<Payment>().HasKey(x => x.Id);
        b.Entity<Payment>().HasIndex(x => new { x.GroupId, x.Status, x.DueDate });
        b.Entity<Payment>().HasIndex(x => x.IdempotencyKey).IsUnique();
        b.Entity<TaskItem>().HasKey(x => x.Id);
        b.Entity<TaskItem>().HasIndex(x => new { x.GroupId, x.Status, x.DueDate });
        b.Entity<ShoppingItem>().HasKey(x => x.Id);
        b.Entity<ShoppingItem>().HasIndex(x => new { x.GroupId, x.Status });
        b.Entity<ShoppingWatcher>().HasKey(x => new { x.GroupId, x.UserId });
        b.Entity<Renewal>().HasKey(x => x.Id);
        b.Entity<Renewal>().HasIndex(x => new { x.GroupId, x.ExpiresOn });
        b.Entity<Chore>().HasKey(x => x.Id);
        b.Entity<Chore>().HasIndex(x => new { x.GroupId, x.Status });
        b.Entity<InvoiceDocument>().HasKey(x => x.Id);
    }
}
