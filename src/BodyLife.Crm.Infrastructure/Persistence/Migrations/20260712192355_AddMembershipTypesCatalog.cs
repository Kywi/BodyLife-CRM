using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BodyLife.Crm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMembershipTypesCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "membership_types",
                schema: "bodylife",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    duration_days = table.Column<int>(type: "integer", nullable: false),
                    visits_limit = table.Column<int>(type: "integer", nullable: false),
                    price_amount = table.Column<decimal>(type: "numeric", nullable: false),
                    price_currency = table.Column<string>(type: "text", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    comment = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deactivated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_membership_types", x => x.id);
                    table.CheckConstraint("ck_membership_types_comment_not_empty", "comment is null or length(btrim(comment)) > 0");
                    table.CheckConstraint("ck_membership_types_currency_canonical", "length(btrim(price_currency)) > 0 and price_currency = upper(btrim(price_currency))");
                    table.CheckConstraint("ck_membership_types_duration_positive", "duration_days > 0");
                    table.CheckConstraint("ck_membership_types_lifecycle", "(\n    is_active\n    and deactivated_at is null\n)\nor (\n    not is_active\n    and deactivated_at is not null\n    and deactivated_at >= created_at\n    and deactivated_at <= updated_at\n)");
                    table.CheckConstraint("ck_membership_types_name_not_empty", "length(btrim(name)) > 0");
                    table.CheckConstraint("ck_membership_types_price_non_negative", "price_amount >= 0");
                    table.CheckConstraint("ck_membership_types_updated_after_created", "updated_at >= created_at");
                    table.CheckConstraint("ck_membership_types_visits_non_negative", "visits_limit >= 0");
                });

            migrationBuilder.CreateIndex(
                name: "ix_membership_types_active_issue_order",
                schema: "bodylife",
                table: "membership_types",
                columns: new[] { "name", "id" },
                filter: "is_active");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "membership_types",
                schema: "bodylife");
        }
    }
}
