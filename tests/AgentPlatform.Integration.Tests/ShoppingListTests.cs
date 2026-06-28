using AgentPlatform.Plugins.Family.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AgentPlatform.Integration.Tests;

[Collection("postgres")]
public class ShoppingListTests(PostgresFixture fx)
{
    private FamilyDbContext Db() => new(new DbContextOptionsBuilder<FamilyDbContext>()
        .UseNpgsql(fx.ConnectionString).UseSnakeCaseNamingConvention().Options);

    [Fact]
    public async Task SharedList_Add_List_FirstWinsMark()
    {
        await using var db = Db();
        var repo = new ShoppingRepository(db);
        var group = Guid.NewGuid();
        var u1 = Guid.NewGuid();
        var u2 = Guid.NewGuid();
        var ct = CancellationToken.None;

        await repo.AddItemAsync(new ShoppingItem { GroupId = group, Name = "mleko", AddedBy = u1 }, ct);
        await repo.AddItemAsync(new ShoppingItem { GroupId = group, Name = "chleb", AddedBy = u2 }, ct);
        Assert.Equal(2, (await repo.ListNeededAsync(group, ct)).Count);

        var first = await repo.MarkBoughtAsync(group, "mleko", u2, ct);
        Assert.NotNull(first);
        Assert.Equal(u2, first!.BoughtBy);
        var second = await repo.MarkBoughtAsync(group, "mleko", u1, ct);
        Assert.Null(second); // already bought — first wins
        Assert.Single(await repo.ListNeededAsync(group, ct)); // chleb left
    }

    [Fact]
    public async Task Watchers_Standing_And_Expiring()
    {
        await using var db = Db();
        var repo = new ShoppingRepository(db);
        var group = Guid.NewGuid();
        var standing = Guid.NewGuid();
        var trip = Guid.NewGuid();
        var expired = Guid.NewGuid();
        var ct = CancellationToken.None;

        await repo.AddWatcherAsync(group, standing, null, ct);                                   // standing
        await repo.AddWatcherAsync(group, trip, DateTimeOffset.UtcNow.AddHours(4), ct);          // active trip
        await repo.AddWatcherAsync(group, expired, DateTimeOffset.UtcNow.AddHours(-1), ct);      // expired

        var active = await repo.ActiveWatchersAsync(group, ct);
        Assert.Contains(standing, active);
        Assert.Contains(trip, active);
        Assert.DoesNotContain(expired, active);

        await repo.RemoveWatcherAsync(group, standing, ct);
        Assert.DoesNotContain(standing, await repo.ActiveWatchersAsync(group, ct));
    }
}
