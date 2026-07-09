using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BodyLife.Crm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUsersRolesAccountsSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "accounts",
                schema: "bodylife",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    account_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deactivated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounts", x => x.id);
                    table.CheckConstraint("ck_accounts_account_type", "account_type in ('owner', 'named_admin', 'shared_reception_admin')");
                    table.CheckConstraint("ck_accounts_account_type_role", "(account_type = 'owner' and role = 'owner')\nor (account_type in ('named_admin', 'shared_reception_admin') and role = 'admin')");
                    table.CheckConstraint("ck_accounts_active_deactivated_at", "(is_active and deactivated_at is null) or (not is_active and deactivated_at is not null)");
                    table.CheckConstraint("ck_accounts_deactivated_at_after_created", "deactivated_at is null or deactivated_at >= created_at");
                    table.CheckConstraint("ck_accounts_display_name_not_empty", "length(btrim(display_name)) > 0");
                    table.CheckConstraint("ck_accounts_role", "role in ('owner', 'admin')");
                });

            migrationBuilder.CreateTable(
                name: "sessions",
                schema: "bodylife",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_label = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sessions", x => x.id);
                    table.CheckConstraint("ck_sessions_ended_at_after_started", "ended_at is null or ended_at >= started_at");
                    table.CheckConstraint("ck_sessions_last_seen_after_started", "last_seen_at >= started_at");
                    table.ForeignKey(
                        name: "FK_sessions_accounts_account_id",
                        column: x => x.account_id,
                        principalSchema: "bodylife",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_accounts_active_type",
                schema: "bodylife",
                table: "accounts",
                columns: new[] { "is_active", "account_type" });

            migrationBuilder.CreateIndex(
                name: "ux_accounts_single_owner",
                schema: "bodylife",
                table: "accounts",
                column: "account_type",
                unique: true,
                filter: "account_type = 'owner'");

            migrationBuilder.CreateIndex(
                name: "ix_sessions_active_account_started_at",
                schema: "bodylife",
                table: "sessions",
                columns: new[] { "account_id", "started_at" },
                filter: "ended_at is null");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sessions",
                schema: "bodylife");

            migrationBuilder.DropTable(
                name: "accounts",
                schema: "bodylife");
        }
    }
}
