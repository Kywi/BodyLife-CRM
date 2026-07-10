using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BodyLife.Crm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDuplicateWarningAcknowledgements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "duplicate_warning_acknowledgements",
                schema: "bodylife",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    warning_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    matched_client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    acknowledged_by_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    acknowledged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_duplicate_warning_acknowledgements", x => x.id);
                    table.CheckConstraint("ck_duplicate_warning_acknowledgements_distinct_clients", "client_id <> matched_client_id");
                    table.CheckConstraint("ck_duplicate_warning_acknowledgements_reason_not_empty", "length(btrim(reason)) > 0");
                    table.CheckConstraint("ck_duplicate_warning_acknowledgements_warning_type", "warning_type in ('duplicate_phone', 'similar_name')");
                    table.ForeignKey(
                        name: "fk_duplicate_warning_acks_actor",
                        column: x => x.acknowledged_by_account_id,
                        principalSchema: "bodylife",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_duplicate_warning_acks_client",
                        column: x => x.client_id,
                        principalSchema: "bodylife",
                        principalTable: "clients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_duplicate_warning_acks_matched_client",
                        column: x => x.matched_client_id,
                        principalSchema: "bodylife",
                        principalTable: "clients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_duplicate_warning_acks_actor",
                schema: "bodylife",
                table: "duplicate_warning_acknowledgements",
                column: "acknowledged_by_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_duplicate_warning_acks_client_timeline",
                schema: "bodylife",
                table: "duplicate_warning_acknowledgements",
                columns: new[] { "client_id", "acknowledged_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_duplicate_warning_acks_match_timeline",
                schema: "bodylife",
                table: "duplicate_warning_acknowledgements",
                columns: new[] { "matched_client_id", "acknowledged_at" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "duplicate_warning_acknowledgements",
                schema: "bodylife");
        }
    }
}
