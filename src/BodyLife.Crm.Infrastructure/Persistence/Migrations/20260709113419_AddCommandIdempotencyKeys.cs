using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BodyLife.Crm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCommandIdempotencyKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "bodylife");

            migrationBuilder.CreateTable(
                name: "command_idempotency_keys",
                schema: "bodylife",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    command_name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    request_correlation_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_role = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    account_kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: true),
                    device_label = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    entry_origin = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    primary_entity_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reread_target_id = table.Column<Guid>(type: "uuid", nullable: true),
                    audit_entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                    result_fingerprint = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_command_idempotency_keys", x => x.id);
                    table.CheckConstraint("ck_command_idempotency_keys_account_kind_not_empty", "length(btrim(account_kind)) > 0");
                    table.CheckConstraint("ck_command_idempotency_keys_actor_role_not_empty", "length(btrim(actor_role)) > 0");
                    table.CheckConstraint("ck_command_idempotency_keys_command_name_not_empty", "length(btrim(command_name)) > 0");
                    table.CheckConstraint("ck_command_idempotency_keys_completed_after_created", "completed_at is null or completed_at >= created_at");
                    table.CheckConstraint("ck_command_idempotency_keys_completed_status_has_time", "(status = 'started') or completed_at is not null");
                    table.CheckConstraint("ck_command_idempotency_keys_correlation_not_empty", "length(btrim(request_correlation_id)) > 0");
                    table.CheckConstraint("ck_command_idempotency_keys_entry_origin", "entry_origin in ('normal', 'manual_backfill', 'paper_fallback', 'future_import')");
                    table.CheckConstraint("ck_command_idempotency_keys_expires_after_created", "expires_at > created_at");
                    table.CheckConstraint("ck_command_idempotency_keys_key_not_empty", "length(btrim(idempotency_key)) > 0");
                    table.CheckConstraint("ck_command_idempotency_keys_started_not_completed", "(status <> 'started') or completed_at is null");
                    table.CheckConstraint("ck_command_idempotency_keys_status", "status in ('started', 'succeeded', 'failed')");
                });

            migrationBuilder.CreateIndex(
                name: "ix_command_idempotency_keys_expires_at",
                schema: "bodylife",
                table: "command_idempotency_keys",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ux_command_idempotency_keys_command_key",
                schema: "bodylife",
                table: "command_idempotency_keys",
                columns: new[] { "command_name", "idempotency_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "command_idempotency_keys",
                schema: "bodylife");
        }
    }
}
