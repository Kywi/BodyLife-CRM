using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BodyLife.Crm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMembershipStateCache : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "membership_state_cache",
                schema: "bodylife",
                columns: table => new
                {
                    membership_id = table.Column<Guid>(type: "uuid", nullable: false),
                    counted_visits = table.Column<int>(type: "integer", nullable: false),
                    remaining_visits = table.Column<int>(type: "integer", nullable: false),
                    negative_balance = table.Column<int>(type: "integer", nullable: false),
                    first_negative_visit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    first_negative_visit_date = table.Column<DateOnly>(type: "date", nullable: true),
                    extension_days = table.Column<int>(type: "integer", nullable: false),
                    effective_end_date = table.Column<DateOnly>(type: "date", nullable: false),
                    last_counted_visit_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    recalculated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recalculation_version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_membership_state_cache", x => x.membership_id);
                    table.CheckConstraint("ck_membership_state_cache_counted_visits_non_negative", "counted_visits >= 0");
                    table.CheckConstraint("ck_membership_state_cache_extension_days_non_negative", "extension_days >= 0");
                    table.CheckConstraint("ck_membership_state_cache_negative_balance_consistent", "negative_balance = greatest(0::bigint, -(remaining_visits::bigint))");
                    table.CheckConstraint("ck_membership_state_cache_recalculation_version_positive", "recalculation_version > 0");
                    table.ForeignKey(
                        name: "FK_membership_state_cache_issued_memberships_membership_id",
                        column: x => x.membership_id,
                        principalSchema: "bodylife",
                        principalTable: "issued_memberships",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_membership_state_cache_effective_end_date",
                schema: "bodylife",
                table: "membership_state_cache",
                column: "effective_end_date");

            migrationBuilder.CreateIndex(
                name: "ix_membership_state_cache_last_counted_visit_at",
                schema: "bodylife",
                table: "membership_state_cache",
                column: "last_counted_visit_at");

            migrationBuilder.CreateIndex(
                name: "ix_membership_state_cache_negative_balance_open",
                schema: "bodylife",
                table: "membership_state_cache",
                column: "negative_balance",
                filter: "negative_balance > 0");

            migrationBuilder.CreateIndex(
                name: "ix_membership_state_cache_remaining_visits",
                schema: "bodylife",
                table: "membership_state_cache",
                column: "remaining_visits");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "membership_state_cache",
                schema: "bodylife");
        }
    }
}
