using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BodyLife.Crm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIssuedMemberships : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "issued_memberships",
                schema: "bodylife",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    membership_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type_name_snapshot = table.Column<string>(type: "text", nullable: false),
                    duration_days_snapshot = table.Column<int>(type: "integer", nullable: false),
                    visits_limit_snapshot = table.Column<int>(type: "integer", nullable: false),
                    price_amount_snapshot = table.Column<decimal>(type: "numeric", nullable: false),
                    price_currency_snapshot = table.Column<string>(type: "text", nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    base_end_date = table.Column<DateOnly>(type: "date", nullable: false),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    issued_by_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    entry_origin = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    entry_batch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    comment = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_issued_memberships", x => x.id);
                    table.CheckConstraint("ck_issued_memberships_base_end_date", "base_end_date = start_date + (duration_days_snapshot - 1)");
                    table.CheckConstraint("ck_issued_memberships_comment_not_empty", "comment is null or length(btrim(comment)) > 0");
                    table.CheckConstraint("ck_issued_memberships_currency_snapshot_canonical", "length(btrim(price_currency_snapshot)) > 0 and price_currency_snapshot = upper(btrim(price_currency_snapshot))");
                    table.CheckConstraint("ck_issued_memberships_duration_snapshot_positive", "duration_days_snapshot > 0");
                    table.CheckConstraint("ck_issued_memberships_entry_origin", "entry_origin in ('normal', 'manual_backfill', 'paper_fallback', 'future_import')");
                    table.CheckConstraint("ck_issued_memberships_price_snapshot_non_negative", "price_amount_snapshot >= 0");
                    table.CheckConstraint("ck_issued_memberships_status", "status in ('active', 'canceled', 'corrected')");
                    table.CheckConstraint("ck_issued_memberships_type_name_snapshot_not_empty", "length(btrim(type_name_snapshot)) > 0");
                    table.CheckConstraint("ck_issued_memberships_visits_snapshot_non_negative", "visits_limit_snapshot >= 0");
                    table.ForeignKey(
                        name: "FK_issued_memberships_accounts_issued_by_account_id",
                        column: x => x.issued_by_account_id,
                        principalSchema: "bodylife",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_issued_memberships_clients_client_id",
                        column: x => x.client_id,
                        principalSchema: "bodylife",
                        principalTable: "clients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_issued_memberships_membership_types_membership_type_id",
                        column: x => x.membership_type_id,
                        principalSchema: "bodylife",
                        principalTable: "membership_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_issued_memberships_client_timeline",
                schema: "bodylife",
                table: "issued_memberships",
                columns: new[] { "client_id", "start_date", "issued_at" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "ix_issued_memberships_issued_by_account_id",
                schema: "bodylife",
                table: "issued_memberships",
                column: "issued_by_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_issued_memberships_membership_type_id",
                schema: "bodylife",
                table: "issued_memberships",
                column: "membership_type_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "issued_memberships",
                schema: "bodylife");
        }
    }
}
