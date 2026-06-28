using System.Text;
using System.Text.Json;
using AgentPlatform.Plugins.Email;
using AgentPlatform.Plugins.Family.Data;
using AgentPlatform.Plugins.Family.Payments;
using AgentPlatform.PluginSdk.Contracts;
using AgentPlatform.PluginSdk.Contracts.Models;
using AgentPlatform.PluginSdk.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using MimeKit;
using Xunit;

namespace AgentPlatform.Core.Tests;

file sealed class FakeInvoiceDocRepo : IInvoiceDocumentRepository
{
    public Task<Guid> CreateAsync(InvoiceDocument doc, CancellationToken ct) => Task.FromResult(doc.Id);
}

file sealed class StubPaymentsRepo(List<Payment> payments) : IPaymentsRepository
{
    public Task<Guid> CreateAsync(Payment p, CancellationToken ct) => Task.FromResult(p.Id);
    public Task<Payment?> FindPendingByCreditorAsync(Guid g, string c, CancellationToken ct) => Task.FromResult<Payment?>(null);
    public Task<Payment?> GetAsync(Guid id, CancellationToken ct) => Task.FromResult<Payment?>(null);
    public Task<bool> MarkPaidAsync(Guid id, Guid u, string k, CancellationToken ct) => Task.FromResult(true);
    public Task<IReadOnlyList<Payment>> ListDueAsync(Guid g, CancellationToken ct) => Task.FromResult<IReadOnlyList<Payment>>([]);
    public Task<IReadOnlyList<Payment>> ListAllAsync(Guid g, CancellationToken ct) => Task.FromResult<IReadOnlyList<Payment>>(payments);
    public Task<IReadOnlyList<Payment>> DueForReminderAsync(Guid g, CancellationToken ct) => Task.FromResult<IReadOnlyList<Payment>>([]);
    public Task<IReadOnlyList<Payment>> DueForEscalationAsync(Guid g, TimeSpan w, CancellationToken ct) => Task.FromResult<IReadOnlyList<Payment>>([]);
    public Task MarkRemindedAsync(Guid id, CancellationToken ct) => Task.CompletedTask;
    public Task MarkEscalatedAsync(Guid id, CancellationToken ct) => Task.CompletedTask;
}

file sealed class StubUserRepo(string? email) : IUserRepository
{
    public Task<UserGroupInfo?> GetByChannelIdentityAsync(string c, string e, CancellationToken ct) => Task.FromResult<UserGroupInfo?>(null);
    public Task<UserGroupInfo?> GetByEmailAsync(string e, CancellationToken ct) =>
        Task.FromResult(e == email ? new UserGroupInfo("u1", "g1", "household", MemberRole.Member, "Test") : null);
    public Task<UserGroupInfo?> GetPrimaryGroupAsync(string u, CancellationToken ct) => Task.FromResult<UserGroupInfo?>(null);
    public Task<bool> SetChannelIdentityAsync(string u, string c, string e, CancellationToken ct) => Task.FromResult(false);
}

public class InvoiceExtractToolTests
{
    [Fact]
    public async Task Extracts_Amount_Creditor_DueDate_FromText()
    {
        var blobs = new InMemoryBlobStorage();
        var text = "Sprzedawca: PGNiG Obrót Detaliczny\nDo zapłaty: 247,89\nTermin: 2026-07-15\n";
        var storageRef = await blobs.StoreAsync(new MemoryStream(Encoding.UTF8.GetBytes(text)), "text/plain", CancellationToken.None);

        var tool = new InvoiceExtractTool(blobs, new FakeInvoiceDocRepo());
        var input = new ToolInput("family.invoice.extract",
            JsonSerializer.SerializeToElement(new { storageRef }));
        var ctx = PluginTestHarness.FakeContext(
            userId: Guid.NewGuid().ToString(), householdId: Guid.NewGuid().ToString());
        var result = await tool.ExecuteAsync(input, ctx, CancellationToken.None);

        Assert.Equal(ToolResultStatus.Success, result.Status);
        var data = result.Data!.Value;
        Assert.Equal(247.89m, data.GetProperty("amount").GetDecimal());
        Assert.Contains("PGNiG", data.GetProperty("creditor").GetString());
        Assert.Equal("2026-07-15", data.GetProperty("dueDate").GetString());
    }
}

public class BillAnomalyProviderTests
{
    [Fact]
    public async Task Flags_Creditor_With_Over30PercentSpike()
    {
        var g = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var payments = new List<Payment>
        {
            new() { GroupId = g, Creditor = "Energa", Amount = 300, CreatedAt = now },          // latest, +50%
            new() { GroupId = g, Creditor = "Energa", Amount = 200, CreatedAt = now.AddMonths(-1) },
            new() { GroupId = g, Creditor = "Energa", Amount = 200, CreatedAt = now.AddMonths(-2) },
        };
        var provider = new BillAnomalyProvider(new StubPaymentsRepo(payments));
        var ctx = new ContextRequest(PluginTestHarness.FakeContext(householdId: g.ToString()), "i", "sprawdź rachunki");

        var slice = await provider.FetchAsync(ctx, CancellationToken.None);
        var json = JsonSerializer.SerializeToElement(slice.Data);
        Assert.True(json.GetProperty("anomalies").GetArrayLength() >= 1);
    }
}

public class EmailParserTests
{
    [Fact]
    public async Task ConfirmationReply_TAK_BecomesConfirmCommand()
    {
        var parser = new EmailParser(new StubUserRepo("user@example.com"), NullLogger<EmailParser>.Instance);
        var msg = new MimeMessage();
        msg.From.Add(new MailboxAddress("User", "user@example.com"));
        msg.Subject = "Re: [Agent] Potwierdzenie wymagane";
        msg.Body = new TextPart("plain") { Text = "TAK\n\n> Ref: abc-123" };

        var parsed = await parser.ParseAsync(msg, CancellationToken.None);
        Assert.NotNull(parsed);
        Assert.StartsWith("confirm:", parsed!.BodyText);
    }

    [Fact]
    public async Task UnknownSender_ReturnsNull()
    {
        var parser = new EmailParser(new StubUserRepo("known@example.com"), NullLogger<EmailParser>.Instance);
        var msg = new MimeMessage();
        msg.From.Add(new MailboxAddress("X", "stranger@example.com"));
        msg.Body = new TextPart("plain") { Text = "hello" };
        Assert.Null(await parser.ParseAsync(msg, CancellationToken.None));
    }
}
