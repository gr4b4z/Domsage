using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentPlatform.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class ChannelIdentities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1) New generic table first.
            migrationBuilder.CreateTable(
                name: "channel_identities",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel_id = table.Column<string>(type: "text", nullable: false),
                    external_id = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_channel_identities", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_channel_identities_channel_id_external_id",
                table: "channel_identities",
                columns: new[] { "channel_id", "external_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_channel_identities_user_id_channel_id",
                table: "channel_identities",
                columns: new[] { "user_id", "channel_id" });

            // 2) Migrate existing per-channel columns into rows (no data loss).
            migrationBuilder.Sql(@"
                INSERT INTO channel_identities (id, channel_id, external_id, user_id)
                SELECT gen_random_uuid(), 'telegram', telegram_id::text, id
                FROM users WHERE telegram_id IS NOT NULL;");
            migrationBuilder.Sql(@"
                INSERT INTO channel_identities (id, channel_id, external_id, user_id)
                SELECT gen_random_uuid(), 'signal', signal_number, id
                FROM users WHERE signal_number IS NOT NULL;");

            // 3) Now drop the channel-specific columns from the core users table.
            migrationBuilder.DropIndex(name: "ix_users_signal_number", table: "users");
            migrationBuilder.DropIndex(name: "ix_users_telegram_id", table: "users");
            migrationBuilder.DropColumn(name: "signal_number", table: "users");
            migrationBuilder.DropColumn(name: "telegram_id", table: "users");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "signal_number",
                table: "users",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "telegram_id",
                table: "users",
                type: "bigint",
                nullable: true);

            // Restore data back onto the columns before dropping the table.
            migrationBuilder.Sql(@"
                UPDATE users u SET telegram_id = ci.external_id::bigint
                FROM channel_identities ci WHERE ci.user_id = u.id AND ci.channel_id = 'telegram';");
            migrationBuilder.Sql(@"
                UPDATE users u SET signal_number = ci.external_id
                FROM channel_identities ci WHERE ci.user_id = u.id AND ci.channel_id = 'signal';");

            migrationBuilder.CreateIndex(
                name: "ix_users_signal_number",
                table: "users",
                column: "signal_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_telegram_id",
                table: "users",
                column: "telegram_id",
                unique: true);

            migrationBuilder.DropTable(
                name: "channel_identities");
        }
    }
}
