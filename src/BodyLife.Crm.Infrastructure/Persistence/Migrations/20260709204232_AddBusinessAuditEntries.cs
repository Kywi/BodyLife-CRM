using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BodyLife.Crm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBusinessAuditEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "business_audit_entries",
                schema: "bodylife",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    action_type = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    entity_type = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    related_entity_refs = table.Column<string>(type: "jsonb", nullable: false),
                    actor_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_account_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    actor_role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_label = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    before_summary = table.Column<string>(type: "jsonb", nullable: false),
                    after_summary = table.Column<string>(type: "jsonb", nullable: false),
                    request_correlation_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    entry_origin = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    changed_after_close = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_business_audit_entries", x => x.id);
                    table.CheckConstraint("ck_business_audit_entries_action_type_not_empty", "length(btrim(action_type)) > 0");
                    table.CheckConstraint("ck_business_audit_entries_actor_account_type", "actor_account_type in ('owner', 'named_admin', 'shared_reception_admin')");
                    table.CheckConstraint("ck_business_audit_entries_actor_role", "actor_role in ('owner', 'admin')");
                    table.CheckConstraint("ck_business_audit_entries_correlation_not_empty", "length(btrim(request_correlation_id)) > 0");
                    table.CheckConstraint("ck_business_audit_entries_entity_type_not_empty", "length(btrim(entity_type)) > 0");
                    table.CheckConstraint("ck_business_audit_entries_entry_origin", "entry_origin in ('normal', 'manual_backfill', 'paper_fallback', 'future_import')");
                });

            migrationBuilder.CreateIndex(
                name: "ix_business_audit_entries_actor_timeline",
                schema: "bodylife",
                table: "business_audit_entries",
                columns: new[] { "actor_account_id", "recorded_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_business_audit_entries_entity_timeline",
                schema: "bodylife",
                table: "business_audit_entries",
                columns: new[] { "entity_type", "entity_id", "recorded_at" },
                descending: new[] { false, false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "business_audit_entries",
                schema: "bodylife");
        }
    }
}
