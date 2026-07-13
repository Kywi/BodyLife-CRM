using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BodyLife.Crm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMembershipOpeningStates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "membership_opening_states",
                schema: "bodylife",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    membership_id = table.Column<Guid>(type: "uuid", nullable: false),
                    opening_as_of_date = table.Column<DateOnly>(type: "date", nullable: false),
                    declared_remaining_visits = table.Column<int>(type: "integer", nullable: false),
                    declared_negative_balance = table.Column<int>(type: "integer", nullable: false),
                    known_effective_end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    known_extension_days = table.Column<int>(type: "integer", nullable: true),
                    source_reference = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
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
                    table.PrimaryKey("PK_membership_opening_states", x => x.id);
                    table.CheckConstraint("ck_membership_opening_states_entry_origin", "entry_origin in ('manual_backfill', 'paper_fallback', 'future_import')");
                    table.CheckConstraint("ck_membership_opening_states_known_end_not_before_opening", "known_effective_end_date is null or known_effective_end_date >= opening_as_of_date");
                    table.CheckConstraint("ck_membership_opening_states_known_extension_days_non_negative", "known_extension_days is null or known_extension_days >= 0");
                    table.CheckConstraint("ck_membership_opening_states_negative_balance_consistent", "declared_negative_balance = greatest(0::bigint, -(declared_remaining_visits::bigint))");
                    table.CheckConstraint("ck_membership_opening_states_reason_not_empty", "length(btrim(reason)) > 0");
                    table.CheckConstraint("ck_membership_opening_states_source_reference_not_empty", "length(btrim(source_reference)) > 0");
                    table.CheckConstraint("ck_membership_opening_states_status", "status in ('active', 'canceled', 'corrected')");
                    table.ForeignKey(
                        name: "FK_membership_opening_states_accounts_recorded_by_account_id",
                        column: x => x.recorded_by_account_id,
                        principalSchema: "bodylife",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_membership_opening_states_issued_memberships_membership_id",
                        column: x => x.membership_id,
                        principalSchema: "bodylife",
                        principalTable: "issued_memberships",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_membership_opening_states_sessions_recorded_session_id",
                        column: x => x.recorded_session_id,
                        principalSchema: "bodylife",
                        principalTable: "sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_membership_opening_states_membership_timeline",
                schema: "bodylife",
                table: "membership_opening_states",
                columns: new[] { "membership_id", "opening_as_of_date", "recorded_at" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "ix_membership_opening_states_recorded_by_account_id",
                schema: "bodylife",
                table: "membership_opening_states",
                column: "recorded_by_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_membership_opening_states_recorded_session_id",
                schema: "bodylife",
                table: "membership_opening_states",
                column: "recorded_session_id");

            migrationBuilder.CreateIndex(
                name: "ux_membership_opening_states_active_membership",
                schema: "bodylife",
                table: "membership_opening_states",
                column: "membership_id",
                unique: true,
                filter: "status = 'active'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "membership_opening_states",
                schema: "bodylife");
        }
    }
}
