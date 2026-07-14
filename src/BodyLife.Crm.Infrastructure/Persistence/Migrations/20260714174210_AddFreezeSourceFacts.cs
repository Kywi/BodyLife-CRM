using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BodyLife.Crm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFreezeSourceFacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "freezes",
                schema: "bodylife",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    membership_id = table.Column<Guid>(type: "uuid", nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_by_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entry_origin = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    entry_batch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_freezes", x => x.id);
                    table.CheckConstraint("ck_freezes_entry_origin", "entry_origin in ('normal', 'manual_backfill', 'paper_fallback', 'future_import')");
                    table.CheckConstraint("ck_freezes_inclusive_range", "start_date <= end_date");
                    table.CheckConstraint("ck_freezes_reason_not_empty", "length(btrim(reason)) > 0");
                    table.CheckConstraint("ck_freezes_status", "status in ('active', 'canceled')");
                    table.ForeignKey(
                        name: "FK_freezes_accounts_recorded_by_account_id",
                        column: x => x.recorded_by_account_id,
                        principalSchema: "bodylife",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_freezes_issued_memberships_membership_client",
                        columns: x => new { x.membership_id, x.client_id },
                        principalSchema: "bodylife",
                        principalTable: "issued_memberships",
                        principalColumns: new[] { "id", "client_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_freezes_sessions_session_id",
                        column: x => x.session_id,
                        principalSchema: "bodylife",
                        principalTable: "sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "freeze_cancellations",
                schema: "bodylife",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    freeze_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_by_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entry_origin = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    entry_batch_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_freeze_cancellations", x => x.id);
                    table.CheckConstraint("ck_freeze_cancellations_entry_origin", "entry_origin in ('normal', 'manual_backfill', 'paper_fallback', 'future_import')");
                    table.CheckConstraint("ck_freeze_cancellations_reason_not_empty", "length(btrim(reason)) > 0");
                    table.ForeignKey(
                        name: "FK_freeze_cancellations_accounts_recorded_by_account_id",
                        column: x => x.recorded_by_account_id,
                        principalSchema: "bodylife",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_freeze_cancellations_freezes_freeze_id",
                        column: x => x.freeze_id,
                        principalSchema: "bodylife",
                        principalTable: "freezes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_freeze_cancellations_sessions_session_id",
                        column: x => x.session_id,
                        principalSchema: "bodylife",
                        principalTable: "sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_freeze_cancellations_recorded_by_account_id",
                schema: "bodylife",
                table: "freeze_cancellations",
                column: "recorded_by_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_freeze_cancellations_session_id",
                schema: "bodylife",
                table: "freeze_cancellations",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "ix_freeze_cancellations_timeline",
                schema: "bodylife",
                table: "freeze_cancellations",
                columns: new[] { "occurred_at", "recorded_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ux_freeze_cancellations_freeze_id",
                schema: "bodylife",
                table: "freeze_cancellations",
                column: "freeze_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_freezes_client_timeline",
                schema: "bodylife",
                table: "freezes",
                columns: new[] { "client_id", "recorded_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_freezes_membership_id_client_id",
                schema: "bodylife",
                table: "freezes",
                columns: new[] { "membership_id", "client_id" });

            migrationBuilder.CreateIndex(
                name: "ix_freezes_membership_status_range",
                schema: "bodylife",
                table: "freezes",
                columns: new[] { "membership_id", "status", "start_date", "end_date" });

            migrationBuilder.CreateIndex(
                name: "ix_freezes_recorded_by_account_id",
                schema: "bodylife",
                table: "freezes",
                column: "recorded_by_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_freezes_session_id",
                schema: "bodylife",
                table: "freezes",
                column: "session_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "freeze_cancellations",
                schema: "bodylife");

            migrationBuilder.DropTable(
                name: "freezes",
                schema: "bodylife");
        }
    }
}
