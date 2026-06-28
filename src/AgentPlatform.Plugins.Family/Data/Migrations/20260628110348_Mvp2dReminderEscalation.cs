using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentPlatform.Plugins.Family.Data.Migrations
{
    /// <inheritdoc />
    public partial class Mvp2dReminderEscalation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "escalated_at",
                schema: "family",
                table: "renewals",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "escalated_at",
                schema: "family",
                table: "payments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "lead_days",
                schema: "family",
                table: "payments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "reminded_at",
                schema: "family",
                table: "payments",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "escalated_at",
                schema: "family",
                table: "renewals");

            migrationBuilder.DropColumn(
                name: "escalated_at",
                schema: "family",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "lead_days",
                schema: "family",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "reminded_at",
                schema: "family",
                table: "payments");
        }
    }
}
