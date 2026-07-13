using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BodyLife.Crm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMembershipAdjustments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "membership_adjustments",
                schema: "bodylife",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    membership_id = table.Column<Guid>(type: "uuid", nullable: false),
                    adjustment_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    days_delta = table.Column<int>(type: "integer", nullable: true),
                    visits_delta = table.Column<int>(type: "integer", nullable: true),
                    money_delta = table.Column<decimal>(type: "numeric", nullable: true),
                    effective_date = table.Column<DateOnly>(type: "date", nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_by_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recorded_session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entry_origin = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    entry_batch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_membership_adjustments", x => x.id);
                    table.CheckConstraint("ck_membership_adjustments_adjustment_type_not_empty", "length(btrim(adjustment_type)) > 0");
                    table.CheckConstraint("ck_membership_adjustments_delta_non_zero", "coalesce(days_delta, 0) <> 0 or coalesce(visits_delta, 0) <> 0 or coalesce(money_delta, 0) <> 0");
                    table.CheckConstraint("ck_membership_adjustments_entry_origin", "entry_origin in ('normal', 'manual_backfill', 'paper_fallback', 'future_import')");
                    table.CheckConstraint("ck_membership_adjustments_reason_not_empty", "length(btrim(reason)) > 0");
                    table.CheckConstraint("ck_membership_adjustments_status", "status in ('active', 'canceled', 'corrected')");
                    table.ForeignKey(
                        name: "FK_membership_adjustments_accounts_recorded_by_account_id",
                        column: x => x.recorded_by_account_id,
                        principalSchema: "bodylife",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_membership_adjustments_issued_memberships_membership_id",
                        column: x => x.membership_id,
                        principalSchema: "bodylife",
                        principalTable: "issued_memberships",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_membership_adjustments_sessions_recorded_session_id",
                        column: x => x.recorded_session_id,
                        principalSchema: "bodylife",
                        principalTable: "sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_membership_adjustments_active_membership_effective_date",
                schema: "bodylife",
                table: "membership_adjustments",
                columns: new[] { "membership_id", "effective_date", "adjustment_type" },
                filter: "status = 'active'");

            migrationBuilder.CreateIndex(
                name: "ix_membership_adjustments_membership_timeline",
                schema: "bodylife",
                table: "membership_adjustments",
                columns: new[] { "membership_id", "effective_date", "recorded_at" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "ix_membership_adjustments_recorded_by_account_id",
                schema: "bodylife",
                table: "membership_adjustments",
                column: "recorded_by_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_membership_adjustments_recorded_session_id",
                schema: "bodylife",
                table: "membership_adjustments",
                column: "recorded_session_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "membership_adjustments",
                schema: "bodylife");
        }
    }
}
