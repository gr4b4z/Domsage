using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentPlatform.Plugins.Family.Data.Migrations
{
    /// <inheritdoc />
    public partial class Mvp3Invoices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "invoice_documents",
                schema: "family",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    storage_ref = table.Column<string>(type: "text", nullable: false),
                    media_type = table.Column<string>(type: "text", nullable: false),
                    original_name = table.Column<string>(type: "text", nullable: true),
                    uploaded_by = table.Column<Guid>(type: "uuid", nullable: false),
                    uploaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invoice_documents", x => x.id);
                });

            migrationBuilder.Sql("ALTER TABLE family.invoice_documents ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE family.invoice_documents FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql(
                "CREATE POLICY group_isolation ON family.invoice_documents USING (" +
                "group_id = current_setting('app.current_group_id', true)::uuid);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "invoice_documents",
                schema: "family");
        }
    }
}
