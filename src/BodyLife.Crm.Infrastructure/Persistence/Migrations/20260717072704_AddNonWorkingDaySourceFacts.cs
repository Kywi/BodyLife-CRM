using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BodyLife.Crm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNonWorkingDaySourceFacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "non_working_periods",
                schema: "bodylife",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: false),
                    reason_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    reason_comment = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_non_working_periods", x => x.id);
                    table.UniqueConstraint("AK_non_working_periods_id_range", x => new { x.id, x.start_date, x.end_date });
                    table.CheckConstraint("ck_non_working_periods_inclusive_range", "start_date <= end_date");
                    table.CheckConstraint("ck_non_working_periods_reason_code_not_empty", "length(btrim(reason_code)) > 0");
                    table.CheckConstraint("ck_non_working_periods_reason_comment_not_empty", "reason_comment is null or length(btrim(reason_comment)) > 0");
                    table.CheckConstraint("ck_non_working_periods_status", "status in ('active', 'canceled', 'corrected')");
                    table.ForeignKey(
                        name: "FK_non_working_periods_accounts_created_by_account_id",
                        column: x => x.created_by_account_id,
                        principalSchema: "bodylife",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_non_working_periods_sessions_session_id",
                        column: x => x.session_id,
                        principalSchema: "bodylife",
                        principalTable: "sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "non_working_period_applications",
                schema: "bodylife",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    non_working_period_id = table.Column<Guid>(type: "uuid", nullable: false),
                    membership_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    applied_start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    applied_end_date = table.Column<DateOnly>(type: "date", nullable: false),
                    previewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_non_working_period_applications", x => x.id);
                    table.CheckConstraint("ck_non_working_period_applications_inclusive_range", "applied_start_date <= applied_end_date");
                    table.CheckConstraint("ck_non_working_period_applications_preview_order", "previewed_at <= confirmed_at");
                    table.CheckConstraint("ck_non_working_period_applications_status", "status in ('active', 'canceled', 'corrected')");
                    table.ForeignKey(
                        name: "FK_non_working_period_applications_membership_client",
                        columns: x => new { x.membership_id, x.client_id },
                        principalSchema: "bodylife",
                        principalTable: "issued_memberships",
                        principalColumns: new[] { "id", "client_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_non_working_period_applications_period_range",
                        columns: x => new { x.non_working_period_id, x.applied_start_date, x.applied_end_date },
                        principalSchema: "bodylife",
                        principalTable: "non_working_periods",
                        principalColumns: new[] { "id", "start_date", "end_date" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "non_working_period_cancellations",
                schema: "bodylife",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    non_working_period_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_by_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_non_working_period_cancellations", x => x.id);
                    table.CheckConstraint("ck_non_working_period_cancellations_reason_not_empty", "length(btrim(reason)) > 0");
                    table.ForeignKey(
                        name: "FK_non_working_period_cancellations_accounts_recorded_by_accou~",
                        column: x => x.recorded_by_account_id,
                        principalSchema: "bodylife",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_non_working_period_cancellations_period",
                        column: x => x.non_working_period_id,
                        principalSchema: "bodylife",
                        principalTable: "non_working_periods",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_non_working_period_cancellations_sessions_session_id",
                        column: x => x.session_id,
                        principalSchema: "bodylife",
                        principalTable: "sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_non_working_applications_client_timeline",
                schema: "bodylife",
                table: "non_working_period_applications",
                columns: new[] { "client_id", "confirmed_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_non_working_applications_membership_status_range",
                schema: "bodylife",
                table: "non_working_period_applications",
                columns: new[] { "membership_id", "status", "applied_start_date", "applied_end_date" });

            migrationBuilder.CreateIndex(
                name: "IX_non_working_period_applications_membership_id_client_id",
                schema: "bodylife",
                table: "non_working_period_applications",
                columns: new[] { "membership_id", "client_id" });

            migrationBuilder.CreateIndex(
                name: "IX_non_working_period_applications_non_working_period_id_appli~",
                schema: "bodylife",
                table: "non_working_period_applications",
                columns: new[] { "non_working_period_id", "applied_start_date", "applied_end_date" });

            migrationBuilder.CreateIndex(
                name: "ux_non_working_applications_active_period_membership",
                schema: "bodylife",
                table: "non_working_period_applications",
                columns: new[] { "non_working_period_id", "membership_id" },
                unique: true,
                filter: "status = 'active'");

            migrationBuilder.CreateIndex(
                name: "ix_non_working_period_cancellations_account_id",
                schema: "bodylife",
                table: "non_working_period_cancellations",
                column: "recorded_by_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_non_working_period_cancellations_session_id",
                schema: "bodylife",
                table: "non_working_period_cancellations",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "ix_non_working_period_cancellations_timeline",
                schema: "bodylife",
                table: "non_working_period_cancellations",
                columns: new[] { "recorded_at", "non_working_period_id" },
                descending: new[] { true, false });

            migrationBuilder.CreateIndex(
                name: "ux_non_working_period_cancellations_period_id",
                schema: "bodylife",
                table: "non_working_period_cancellations",
                column: "non_working_period_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_non_working_periods_created_by_account_id",
                schema: "bodylife",
                table: "non_working_periods",
                column: "created_by_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_non_working_periods_session_id",
                schema: "bodylife",
                table: "non_working_periods",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "ix_non_working_periods_status_range",
                schema: "bodylife",
                table: "non_working_periods",
                columns: new[] { "status", "start_date", "end_date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "non_working_period_applications",
                schema: "bodylife");

            migrationBuilder.DropTable(
                name: "non_working_period_cancellations",
                schema: "bodylife");

            migrationBuilder.DropTable(
                name: "non_working_periods",
                schema: "bodylife");
        }
    }
}
