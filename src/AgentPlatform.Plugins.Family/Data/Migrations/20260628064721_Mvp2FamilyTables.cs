using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentPlatform.Plugins.Family.Data.Migrations
{
    /// <inheritdoc />
    public partial class Mvp2FamilyTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "chores",
                schema: "family",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assigned_to = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    r_rule = table.Column<string>(type: "text", nullable: true),
                    allowance_amount = table.Column<decimal>(type: "numeric", nullable: true),
                    allowance_currency = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    confirmed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chores", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "renewals",
                schema: "family",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category = table.Column<string>(type: "text", nullable: false),
                    label = table.Column<string>(type: "text", nullable: false),
                    expires_on = table.Column<DateOnly>(type: "date", nullable: false),
                    lead_days = table.Column<int>(type: "integer", nullable: false),
                    escalate_days = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    last_reminded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_renewals", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "shopping_items",
                schema: "family",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    quantity = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false),
                    added_by = table.Column<Guid>(type: "uuid", nullable: false),
                    bought_by = table.Column<Guid>(type: "uuid", nullable: true),
                    bought_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shopping_items", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_chores_group_id_status",
                schema: "family",
                table: "chores",
                columns: new[] { "group_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_renewals_group_id_expires_on",
                schema: "family",
                table: "renewals",
                columns: new[] { "group_id", "expires_on" });

            migrationBuilder.CreateIndex(
                name: "ix_shopping_items_group_id_status",
                schema: "family",
                table: "shopping_items",
                columns: new[] { "group_id", "status" });

            foreach (var table in new[] { "shopping_items", "renewals", "chores" })
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
                name: "chores",
                schema: "family");

            migrationBuilder.DropTable(
                name: "renewals",
                schema: "family");

            migrationBuilder.DropTable(
                name: "shopping_items",
                schema: "family");
        }
    }
}
