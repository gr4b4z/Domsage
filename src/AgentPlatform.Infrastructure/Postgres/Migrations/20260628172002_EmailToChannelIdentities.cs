using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentPlatform.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class EmailToChannelIdentities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1) New flag on the generic identities table.
            migrationBuilder.AddColumn<bool>(
                name: "is_primary",
                table: "channel_identities",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // 2) Migrate each user's email into a channel identity (primary). No data loss.
            migrationBuilder.Sql(@"
                INSERT INTO channel_identities (id, channel_id, external_id, user_id, is_primary)
                SELECT gen_random_uuid(), 'email', lower(email), id, true
                FROM users WHERE email IS NOT NULL
                ON CONFLICT (channel_id, external_id) DO NOTHING;");

            // 3) Drop the now-redundant column + its index.
            migrationBuilder.DropIndex(name: "ix_users_email", table: "users");
            migrationBuilder.DropColumn(name: "email", table: "users");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "email",
                table: "users",
                type: "text",
                nullable: true);

            // Restore the primary email back onto users before removing the identities.
            migrationBuilder.Sql(@"
                UPDATE users u SET email = ci.external_id
                FROM channel_identities ci
                WHERE ci.user_id = u.id AND ci.channel_id = 'email' AND ci.is_primary;");
            migrationBuilder.Sql("DELETE FROM channel_identities WHERE channel_id = 'email';");

            migrationBuilder.CreateIndex(
                name: "ix_users_email",
                table: "users",
                column: "email",
                unique: true);

            migrationBuilder.DropColumn(
                name: "is_primary",
                table: "channel_identities");
        }
    }
}
