using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BodyLife.Crm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMembershipExtensionDays : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "membership_extension_days",
                schema: "bodylife",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    membership_id = table.Column<Guid>(type: "uuid", nullable: false),
                    extension_date = table.Column<DateOnly>(type: "date", nullable: false),
                    source_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_label = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    recalculated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_membership_extension_days", x => x.id);
                    table.CheckConstraint("ck_membership_extension_days_source_label_not_empty", "length(btrim(source_label)) > 0");
                    table.CheckConstraint("ck_membership_extension_days_source_type_not_empty", "length(btrim(source_type)) > 0");
                    table.ForeignKey(
                        name: "FK_membership_extension_days_issued_memberships_membership_id",
                        column: x => x.membership_id,
                        principalSchema: "bodylife",
                        principalTable: "issued_memberships",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_membership_extension_days_active_membership_date",
                schema: "bodylife",
                table: "membership_extension_days",
                columns: new[] { "membership_id", "extension_date" },
                filter: "is_active");

            migrationBuilder.CreateIndex(
                name: "ix_membership_extension_days_source",
                schema: "bodylife",
                table: "membership_extension_days",
                columns: new[] { "source_type", "source_id" });

            migrationBuilder.CreateIndex(
                name: "ux_membership_extension_days_membership_date_source",
                schema: "bodylife",
                table: "membership_extension_days",
                columns: new[] { "membership_id", "extension_date", "source_type", "source_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "membership_extension_days",
                schema: "bodylife");
        }
    }
}
