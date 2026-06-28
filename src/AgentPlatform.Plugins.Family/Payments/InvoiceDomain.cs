using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AgentPlatform.Plugins.Family.Data;
using AgentPlatform.PluginSdk.Contracts;
using AgentPlatform.PluginSdk.Contracts.Models;
using Json.Schema;

namespace AgentPlatform.Plugins.Family.Payments;

/// <summary>
/// family.invoice.extract — deterministic extraction (NO LLM). Reads the stored document text
/// from blob storage and parses amount/creditor/due date with regex. Records the invoice doc.
/// (OCR for image/PDF binaries is a pluggable extension — text and text-PDF parse here.)
/// </summary>
public sealed partial class InvoiceExtractTool(IBlobStorage blobs, IInvoiceDocumentRepository docs) : ITool
{
    public string ToolId => "family.invoice.extract";
    public bool HasSideEffects => true;
    public ScopeRequirement RequiredScope => new(ContextScope.Group, MemberRole.Member);
    public JsonSchema InputSchema => new JsonSchemaBuilder().Type(SchemaValueType.Object)
        .Properties(("storageRef", new JsonSchemaBuilder().Type(SchemaValueType.String)))
        .Required("storageRef").Build();

    public async Task<ToolResult> ExecuteAsync(ToolInput input, ExecutionContext ctx, CancellationToken ct)
    {
        var storageRef = input.Arguments.GetProperty("storageRef").GetString()!;
        string text;
        await using (var stream = await blobs.ReadAsync(storageRef, ct))
        using (var reader = new StreamReader(stream, Encoding.UTF8))
            text = await reader.ReadToEndAsync(ct);

        var amount = AmountRegex().Match(text) is { Success: true } am
            ? decimal.Parse(am.Groups[1].Value.Replace(",", "."), System.Globalization.CultureInfo.InvariantCulture)
            : (decimal?)null;
        var creditor = CreditorRegex().Match(text) is { Success: true } cm ? cm.Groups[1].Value.Trim() : null;
        var due = DateRegex().Match(text) is { Success: true } dm && DateOnly.TryParse(dm.Groups[1].Value, out var d)
            ? d : (DateOnly?)null;
        var confidence = (amount is not null ? 0.5 : 0) + (creditor is not null ? 0.3 : 0) + (due is not null ? 0.2 : 0);

        var docId = await docs.CreateAsync(new InvoiceDocument
        {
            GroupId = Guid.Parse(ctx.GroupId), StorageRef = storageRef,
            MediaType = "text/plain", UploadedBy = Guid.Parse(ctx.UserId)
        }, ct);

        return new ToolResult(ToolResultStatus.Success, JsonSerializer.SerializeToElement(new
        {
            docId, amount, creditor, dueDate = due?.ToString("yyyy-MM-dd"), currency = "PLN", confidence
        }), null);
    }

    [GeneratedRegex(@"(?:kwota|do zapłaty|razem|suma)[:\s]*([\d\s]+[,.]\d{2})", RegexOptions.IgnoreCase)]
    private static partial Regex AmountRegex();
    [GeneratedRegex(@"(?:sprzedawca|wystawca|od)[:\s]*([A-Za-zĄĆĘŁŃÓŚŹŻąćęłńóśźż0-9 .\-]{3,40})", RegexOptions.IgnoreCase)]
    private static partial Regex CreditorRegex();
    [GeneratedRegex(@"(?:termin|płatność do|do dnia)[:\s]*(\d{4}-\d{2}-\d{2})", RegexOptions.IgnoreCase)]
    private static partial Regex DateRegex();
}

public sealed class InvoiceReceivedHandler : IIntentHandler
{
    public string IntentId => "family.invoice_received";
    public PlannerMode Mode => PlannerMode.ToolCalling;
    public string[] RequiredContextProviders => [];
    public string[] AllowedTools => ["family.invoice.extract", "family.payments.create"];
    public string PromptTemplateId => "invoice_received_v1";
    public ModelTier PreferredTier => ModelTier.Large;
    public ConfirmationPolicy Confirmation => ConfirmationPolicy.Required;
}
