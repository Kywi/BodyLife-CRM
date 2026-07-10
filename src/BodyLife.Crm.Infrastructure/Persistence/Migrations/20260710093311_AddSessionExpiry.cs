using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BodyLife.Crm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSessionExpiry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_sessions_active_account_started_at",
                schema: "bodylife",
                table: "sessions");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "expires_at",
                schema: "bodylife",
                table: "sessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql(
                """
                update bodylife.sessions
                set expires_at = last_seen_at + interval '12 hours'
                where expires_at is null;
                """);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "expires_at",
                schema: "bodylife",
                table: "sessions",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_sessions_active_account_expires_at",
                schema: "bodylife",
                table: "sessions",
                columns: new[] { "account_id", "expires_at" },
                filter: "ended_at is null");

            migrationBuilder.AddCheckConstraint(
                name: "ck_sessions_expires_after_started",
                schema: "bodylife",
                table: "sessions",
                sql: "expires_at > started_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_sessions_active_account_expires_at",
                schema: "bodylife",
                table: "sessions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_sessions_expires_after_started",
                schema: "bodylife",
                table: "sessions");

            migrationBuilder.DropColumn(
                name: "expires_at",
                schema: "bodylife",
                table: "sessions");

            migrationBuilder.CreateIndex(
                name: "ix_sessions_active_account_started_at",
                schema: "bodylife",
                table: "sessions",
                columns: new[] { "account_id", "started_at" },
                filter: "ended_at is null");
        }
    }
}
