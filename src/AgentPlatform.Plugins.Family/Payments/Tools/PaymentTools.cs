using System.Text.Json;
using AgentPlatform.Plugins.Family.Data;
using AgentPlatform.PluginSdk.Contracts;
using AgentPlatform.PluginSdk.Contracts.Models;
using Json.Schema;

namespace AgentPlatform.Plugins.Family.Payments.Tools;

/// <summary>family.payments.create — adds a pending payment.</summary>
public sealed class CreatePaymentTool(IPaymentsRepository repo) : ITool
{
    public string ToolId => "family.payments.create";
    public bool HasSideEffects => true;
    public ScopeRequirement RequiredScope => new(ContextScope.Group, MemberRole.Member);

    public JsonSchema InputSchema => new JsonSchemaBuilder()
        .Type(SchemaValueType.Object)
        .Properties(
            ("creditor", new JsonSchemaBuilder().Type(SchemaValueType.String)),
            ("amount", new JsonSchemaBuilder().Type(SchemaValueType.Number)),
            ("currency", new JsonSchemaBuilder().Type(SchemaValueType.String)),
            ("dueDate", new JsonSchemaBuilder().Type(SchemaValueType.String)))
        .Required("creditor", "amount")
        .Build();

    public async Task<ToolResult> ExecuteAsync(ToolInput input, ExecutionContext ctx, CancellationToken ct)
    {
        var a = input.Arguments;
        var creditor = a.GetProperty("creditor").GetString()!;
        var amount = a.GetProperty("amount").GetDecimal();
        var currency = a.TryGetProperty("currency", out var c) ? c.GetString() ?? "PLN" : "PLN";
        var due = a.TryGetProperty("dueDate", out var d) && DateOnly.TryParse(d.GetString(), out var dd)
            ? dd : DateOnly.FromDateTime(DateTime.UtcNow.AddDays(14));

        var id = await repo.CreateAsync(new Payment
        {
            GroupId = Guid.Parse(ctx.GroupId),
            Creditor = creditor,
            Amount = amount,
            Currency = currency,
            DueDate = due,
        }, ct);

        return new ToolResult(ToolResultStatus.Success,
            JsonSerializer.SerializeToElement(new { id, creditor, amount, currency, dueDate = due.ToString("yyyy-MM-dd") }),
            null, $"✅ Dodano płatność: {creditor} {amount} {currency} (termin {due:yyyy-MM-dd}).");
    }
}

/// <summary>family.payments.mark_paid — marks a pending payment as paid (first confirmation wins).</summary>
public sealed class MarkPaymentPaidTool(IPaymentsRepository repo) : ITool
{
    public string ToolId => "family.payments.mark_paid";
    public bool HasSideEffects => true;
    public ScopeRequirement RequiredScope => new(ContextScope.Group, MemberRole.Member);

    public JsonSchema InputSchema => new JsonSchemaBuilder()
        .Type(SchemaValueType.Object)
        .Properties(
            ("paymentId", new JsonSchemaBuilder().Type(SchemaValueType.String)),
            ("creditor", new JsonSchemaBuilder().Type(SchemaValueType.String)))
        .Build();

    public async Task<ToolResult> ExecuteAsync(ToolInput input, ExecutionContext ctx, CancellationToken ct)
    {
        var groupId = Guid.Parse(ctx.GroupId);
        Payment? payment = null;
        if (input.Arguments.TryGetProperty("paymentId", out var pid) && Guid.TryParse(pid.GetString(), out var gid))
            payment = await repo.GetAsync(gid, ct);
        else if (input.Arguments.TryGetProperty("creditor", out var cr))
            payment = await repo.FindPendingByCreditorAsync(groupId, cr.GetString() ?? "", ct);

        if (payment is null)
            return new ToolResult(ToolResultStatus.Failed, null, "Payment not found",
                "❓ Nie znalazłem pasującej płatności.");

        var ok = await repo.MarkPaidAsync(payment.Id, Guid.Parse(ctx.UserId),
            $"{groupId}:{payment.Id}:mark_paid", ct);

        return ok
            ? new ToolResult(ToolResultStatus.Success,
                JsonSerializer.SerializeToElement(new { paymentId = payment.Id }), null,
                $"✅ Oznaczono jako zapłacone: {payment.Creditor} {payment.Amount} {payment.Currency}.")
            : new ToolResult(ToolResultStatus.Success, null, null,
                $"Już opłacone wcześniej: {payment.Creditor}.");
    }
}
