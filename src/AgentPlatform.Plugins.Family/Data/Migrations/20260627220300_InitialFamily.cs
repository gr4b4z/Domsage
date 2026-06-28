using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentPlatform.Plugins.Family.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialFamily : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "family");

            migrationBuilder.CreateTable(
                name: "payments",
                schema: "family",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    creditor = table.Column<string>(type: "text", nullable: false),
                    amount = table.Column<decimal>(type: "numeric", nullable: false),
                    currency = table.Column<string>(type: "text", nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    paid_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    paid_by = table.Column<Guid>(type: "uuid", nullable: true),
                    idempotency_key = table.Column<string>(type: "text", nullable: true),
                    source = table.Column<string>(type: "text", nullable: false),
                    invoice_doc_id = table.Column<Guid>(type: "uuid", nullable: true),
                    confidence = table.Column<decimal>(type: "numeric", nullable: true),
                    extracted_raw = table.Column<string>(type: "text", nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payments", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tasks",
                schema: "family",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assigned_to = table.Column<Guid>(type: "uuid", nullable: true),
                    title = table.Column<string>(type: "text", nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false),
                    done_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    done_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tasks", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_payments_group_id_status_due_date",
                schema: "family",
                table: "payments",
                columns: new[] { "group_id", "status", "due_date" });

            migrationBuilder.CreateIndex(
                name: "ix_payments_idempotency_key",
                schema: "family",
                table: "payments",
                column: "idempotency_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tasks_group_id_status_due_date",
                schema: "family",
                table: "tasks",
                columns: new[] { "group_id", "status", "due_date" });

            // Row-level security — group isolation keyed on app.current_group_id.
            foreach (var table in new[] { "payments", "tasks" })
            {
                migrationBuilder.Sql($"ALTER TABLE family.{table} ENABLE ROW LEVEL SECURITY;");
                migrationBuilder.Sql($"ALTER TABLE family.{table} FORCE ROW LEVEL SECURITY;");
                migrationBuilder.Sql(
                    $"CREATE POLICY group_isolation ON family.{table} USING (" +
                    "group_id = current_setting('app.current_group_id', true)::uuid);");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payments",
                schema: "family");

            migrationBuilder.DropTable(
                name: "tasks",
                schema: "family");
        }
    }
}
