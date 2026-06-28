using AgentPlatform.Infrastructure.Postgres;
using AgentPlatform.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AgentPlatform.Integration.Tests;

[Collection("postgres")]
public class HistorySearchTests(PostgresFixture fx)
{
    [Fact]
    public async Task SearchMessages_FindsByFullText()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(fx.ConnectionString).UseSnakeCaseNamingConvention().Options;
        await using var db = new AppDbContext(opts);

        var userId = Guid.NewGuid();
        db.Users.Add(new() { Id = userId, DisplayName = "Tester" });
        var conv = new AgentPlatform.Infrastructure.Postgres.Entities.ConversationEntity
        { UserId = userId, ChannelId = "test", Status = "active" };
        db.Conversations.Add(conv);
        db.ConversationMessages.Add(new() { ConversationId = conv.Id, Role = "user", Content = "zapłaciłem OC za samochód w lipcu", Tokens = 8 });
        db.ConversationMessages.Add(new() { ConversationId = conv.Id, Role = "user", Content = "dodaj mleko do listy", Tokens = 5 });
        await db.SaveChangesAsync();

        var repo = new ConversationRepository(db);
        var results = await repo.SearchMessagesAsync(userId.ToString(), "OC samochód", 10, CancellationToken.None);

        Assert.Contains(results, m => m.Content.Contains("OC"));
        Assert.DoesNotContain(results, m => m.Content.Contains("mleko"));
    }
}
