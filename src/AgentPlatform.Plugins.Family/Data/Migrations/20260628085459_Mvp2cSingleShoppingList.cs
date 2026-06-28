using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentPlatform.Plugins.Family.Data.Migrations
{
    /// <inheritdoc />
    public partial class Mvp2cSingleShoppingList : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
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

            migrationBuilder.CreateTable(
                name: "shopping_watchers",
                schema: "family",
                columns: table => new
                {
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shopping_watchers", x => new { x.group_id, x.user_id });
                });

            migrationBuilder.CreateIndex(
                name: "ix_shopping_items_group_id_status",
                schema: "family",
                table: "shopping_items",
                columns: new[] { "group_id", "status" });

            migrationBuilder.Sql("ALTER TABLE family.shopping_watchers ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE family.shopping_watchers FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql(
                "CREATE POLICY group_isolation ON family.shopping_watchers USING (" +
                "group_id = current_setting('app.current_group_id', true)::uuid);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "shopping_watchers",
                schema: "family");

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
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    normalized_name = table.Column<string>(type: "text", nullable: false)
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
        }
    }
}
