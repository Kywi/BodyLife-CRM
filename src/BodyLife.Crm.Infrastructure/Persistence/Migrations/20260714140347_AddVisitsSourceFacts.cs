using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BodyLife.Crm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddVisitsSourceFacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_issued_memberships_id_client_id",
                schema: "bodylife",
                table: "issued_memberships",
                columns: new[] { "id", "client_id" });

            migrationBuilder.CreateTable(
                name: "visits",
                schema: "bodylife",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_by_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    visit_kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    entry_origin = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    entry_batch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    comment = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_visits", x => x.id);
                    table.UniqueConstraint("AK_visits_id_client_id_visit_kind", x => new { x.id, x.client_id, x.visit_kind });
                    table.CheckConstraint("ck_visits_comment_not_empty", "comment is null or length(btrim(comment)) > 0");
                    table.CheckConstraint("ck_visits_entry_origin", "entry_origin in ('normal', 'manual_backfill', 'paper_fallback', 'future_import')");
                    table.CheckConstraint("ck_visits_status", "status in ('active', 'canceled')");
                    table.CheckConstraint("ck_visits_visit_kind", "visit_kind in ('membership', 'one_off', 'trial')");
                    table.ForeignKey(
                        name: "FK_visits_accounts_recorded_by_account_id",
                        column: x => x.recorded_by_account_id,
                        principalSchema: "bodylife",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_visits_clients_client_id",
                        column: x => x.client_id,
                        principalSchema: "bodylife",
                        principalTable: "clients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_visits_sessions_session_id",
                        column: x => x.session_id,
                        principalSchema: "bodylife",
                        principalTable: "sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "visit_cancellations",
                schema: "bodylife",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    visit_id = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("PK_visit_cancellations", x => x.id);
                    table.CheckConstraint("ck_visit_cancellations_entry_origin", "entry_origin in ('normal', 'manual_backfill', 'paper_fallback', 'future_import')");
                    table.CheckConstraint("ck_visit_cancellations_reason_not_empty", "length(btrim(reason)) > 0");
                    table.ForeignKey(
                        name: "FK_visit_cancellations_accounts_recorded_by_account_id",
                        column: x => x.recorded_by_account_id,
                        principalSchema: "bodylife",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_visit_cancellations_sessions_session_id",
                        column: x => x.session_id,
                        principalSchema: "bodylife",
                        principalTable: "sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_visit_cancellations_visits_visit_id",
                        column: x => x.visit_id,
                        principalSchema: "bodylife",
                        principalTable: "visits",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "visit_consumptions",
                schema: "bodylife",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    visit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    visit_kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    membership_id = table.Column<Guid>(type: "uuid", nullable: false),
                    consumption_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    source_fact_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    source_fact_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_by_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recorded_session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_visit_consumptions", x => x.id);
                    table.CheckConstraint("ck_visit_consumptions_consumption_type", "consumption_type = 'counted'");
                    table.CheckConstraint("ck_visit_consumptions_source_fact_identity", "source_fact_id = visit_id");
                    table.CheckConstraint("ck_visit_consumptions_source_fact_type", "source_fact_type = 'visit'");
                    table.CheckConstraint("ck_visit_consumptions_status", "status in ('active', 'canceled')");
                    table.CheckConstraint("ck_visit_consumptions_visit_kind", "visit_kind = 'membership'");
                    table.ForeignKey(
                        name: "FK_visit_consumptions_accounts_recorded_by_account_id",
                        column: x => x.recorded_by_account_id,
                        principalSchema: "bodylife",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_visit_consumptions_issued_memberships_membership_client",
                        columns: x => new { x.membership_id, x.client_id },
                        principalSchema: "bodylife",
                        principalTable: "issued_memberships",
                        principalColumns: new[] { "id", "client_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_visit_consumptions_sessions_recorded_session_id",
                        column: x => x.recorded_session_id,
                        principalSchema: "bodylife",
                        principalTable: "sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_visit_consumptions_visits_visit_client_kind",
                        columns: x => new { x.visit_id, x.client_id, x.visit_kind },
                        principalSchema: "bodylife",
                        principalTable: "visits",
                        principalColumns: new[] { "id", "client_id", "visit_kind" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_visit_cancellations_recorded_by_account_id",
                schema: "bodylife",
                table: "visit_cancellations",
                column: "recorded_by_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_visit_cancellations_session_id",
                schema: "bodylife",
                table: "visit_cancellations",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "ix_visit_cancellations_timeline",
                schema: "bodylife",
                table: "visit_cancellations",
                columns: new[] { "occurred_at", "recorded_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ux_visit_cancellations_visit_id",
                schema: "bodylife",
                table: "visit_cancellations",
                column: "visit_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_visit_consumptions_membership_client",
                schema: "bodylife",
                table: "visit_consumptions",
                columns: new[] { "membership_id", "client_id" });

            migrationBuilder.CreateIndex(
                name: "ix_visit_consumptions_membership_recalculation",
                schema: "bodylife",
                table: "visit_consumptions",
                columns: new[] { "membership_id", "status", "recorded_at", "visit_id" });

            migrationBuilder.CreateIndex(
                name: "ix_visit_consumptions_recorded_by_account_id",
                schema: "bodylife",
                table: "visit_consumptions",
                column: "recorded_by_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_visit_consumptions_recorded_session_id",
                schema: "bodylife",
                table: "visit_consumptions",
                column: "recorded_session_id");

            migrationBuilder.CreateIndex(
                name: "ix_visit_consumptions_visit_client_kind",
                schema: "bodylife",
                table: "visit_consumptions",
                columns: new[] { "visit_id", "client_id", "visit_kind" });

            migrationBuilder.CreateIndex(
                name: "ux_visit_consumptions_active_counted_visit",
                schema: "bodylife",
                table: "visit_consumptions",
                column: "visit_id",
                unique: true,
                filter: "status = 'active' and consumption_type = 'counted'");

            migrationBuilder.CreateIndex(
                name: "ix_visits_active_daily_report",
                schema: "bodylife",
                table: "visits",
                columns: new[] { "occurred_at", "client_id" },
                filter: "status = 'active'");

            migrationBuilder.CreateIndex(
                name: "ix_visits_client_timeline",
                schema: "bodylife",
                table: "visits",
                columns: new[] { "client_id", "occurred_at", "recorded_at" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "ix_visits_recorded_by_account_id",
                schema: "bodylife",
                table: "visits",
                column: "recorded_by_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_visits_session_id",
                schema: "bodylife",
                table: "visits",
                column: "session_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "visit_cancellations",
                schema: "bodylife");

            migrationBuilder.DropTable(
                name: "visit_consumptions",
                schema: "bodylife");

            migrationBuilder.DropTable(
                name: "visits",
                schema: "bodylife");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_issued_memberships_id_client_id",
                schema: "bodylife",
                table: "issued_memberships");
        }
    }
}
