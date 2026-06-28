using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentPlatform.Plugins.Family.Data.Migrations
{
    /// <inheritdoc />
    public partial class Mvp2bShoppingLists : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_shopping_items_group_id_status",
                schema: "family",
                table: "shopping_items");

            migrationBuilder.AddColumn<Guid>(
                name: "list_id",
                schema: "family",
                table: "shopping_items",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "shopping_list_participants",
                schema: "family",
                columns: table => new
                {
                    list_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shopping_list_participants", x => new { x.list_id, x.user_id });
                });

            migrationBuilder.CreateTable(
                name: "shopping_lists",
                schema: "family",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    normalized_name = table.Column<string>(type: "text", nullable: false),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shopping_lists", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_shopping_items_list_id_status",
                schema: "family",
                table: "shopping_items",
                columns: new[] { "list_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_shopping_lists_group_id_last_used_at",
                schema: "family",
                table: "shopping_lists",
                columns: new[] { "group_id", "last_used_at" });

            migrationBuilder.CreateIndex(
                name: "ix_shopping_lists_group_id_normalized_name",
                schema: "family",
                table: "shopping_lists",
                columns: new[] { "group_id", "normalized_name" },
                unique: true);

            // Backfill: one "Ogólna" list per group that already has items, then repoint items.
            migrationBuilder.Sql("""
                INSERT INTO family.shopping_lists (id, group_id, name, normalized_name, last_used_at, created_at)
                SELECT gen_random_uuid(), s.group_id, 'Ogólna', 'ogólna', NOW(), NOW()
                FROM (SELECT DISTINCT group_id FROM family.shopping_items WHERE list_id = '00000000-0000-0000-0000-000000000000') s;
                """);
            migrationBuilder.Sql("""
                UPDATE family.shopping_items i
                SET list_id = l.id
                FROM family.shopping_lists l
                WHERE i.list_id = '00000000-0000-0000-0000-000000000000'
                  AND l.group_id = i.group_id AND l.normalized_name = 'ogólna';
                """);

            // RLS — shopping_lists is group-scoped like the rest of the family schema.
            migrationBuilder.Sql("ALTER TABLE family.shopping_lists ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE family.shopping_lists FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql(
                "CREATE POLICY group_isolation ON family.shopping_lists USING (" +
                "group_id = current_setting('app.current_group_id', true)::uuid);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "shopping_list_participants",
                schema: "family");

            migrationBuilder.DropTable(
                name: "shopping_lists",
                schema: "family");

            migrationBuilder.DropIndex(
                name: "ix_shopping_items_list_id_status",
                schema: "family",
                table: "shopping_items");

            migrationBuilder.DropColumn(
                name: "list_id",
                schema: "family",
                table: "shopping_items");

            migrationBuilder.CreateIndex(
                name: "ix_shopping_items_group_id_status",
                schema: "family",
                table: "shopping_items",
                columns: new[] { "group_id", "status" });
        }
    }
}
