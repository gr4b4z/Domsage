using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AgentPlatform.Plugins.Family.Data;

[Table("invoice_documents")]
public class InvoiceDocument
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid GroupId { get; set; }
    public string StorageRef { get; set; } = "";
    public string MediaType { get; set; } = "";
    public string? OriginalName { get; set; }
    public Guid UploadedBy { get; set; }
    public DateTimeOffset UploadedAt { get; set; } = DateTimeOffset.UtcNow;
}

public interface IInvoiceDocumentRepository
{
    Task<Guid> CreateAsync(InvoiceDocument doc, CancellationToken ct);
}

public sealed class InvoiceDocumentRepository(FamilyDbContext db) : IInvoiceDocumentRepository
{
    public async Task<Guid> CreateAsync(InvoiceDocument doc, CancellationToken ct)
    {
        db.Set<InvoiceDocument>().Add(doc);
        await db.SaveChangesAsync(ct);
        return doc.Id;
    }
}
