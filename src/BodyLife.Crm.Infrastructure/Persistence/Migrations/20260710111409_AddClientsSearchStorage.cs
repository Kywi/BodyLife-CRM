using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BodyLife.Crm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddClientsSearchStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "clients",
                schema: "bodylife",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    surname = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    patronymic = table.Column<string>(type: "text", nullable: true),
                    normalized_full_name = table.Column<string>(type: "text", nullable: false),
                    phone_raw = table.Column<string>(type: "text", nullable: true),
                    phone_normalized = table.Column<string>(type: "text", nullable: true),
                    phone_last4 = table.Column<string>(type: "text", nullable: true),
                    comment = table.Column<string>(type: "text", nullable: true),
                    operational_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_clients", x => x.id);
                    table.CheckConstraint("ck_clients_name_not_empty", "length(btrim(name)) > 0");
                    table.CheckConstraint("ck_clients_normalized_full_name_not_empty", "length(btrim(normalized_full_name)) > 0");
                    table.CheckConstraint("ck_clients_operational_status", "operational_status in ('active', 'inactive')");
                    table.CheckConstraint("ck_clients_patronymic_not_empty", "patronymic is null or length(btrim(patronymic)) > 0");
                    table.CheckConstraint("ck_clients_phone_fields_consistent", "(phone_raw is null and phone_normalized is null and phone_last4 is null)\nor (\n    phone_raw is not null\n    and length(btrim(phone_raw)) > 0\n    and phone_normalized is not null\n    and phone_normalized ~ '^[0-9]{4,}$'\n    and phone_last4 is not null\n    and phone_last4 ~ '^[0-9]{4}$'\n    and phone_last4 = right(phone_normalized, 4)\n)");
                    table.CheckConstraint("ck_clients_surname_not_empty", "length(btrim(surname)) > 0");
                    table.CheckConstraint("ck_clients_updated_after_created", "updated_at >= created_at");
                    table.ForeignKey(
                        name: "FK_clients_accounts_created_by_account_id",
                        column: x => x.created_by_account_id,
                        principalSchema: "bodylife",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "client_card_assignments",
                schema: "bodylife",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    card_number_raw = table.Column<string>(type: "text", nullable: false),
                    card_number_normalized = table.Column<string>(type: "text", nullable: false),
                    assigned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    assigned_by_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ended_by_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    end_reason = table.Column<string>(type: "text", nullable: true),
                    is_current = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_client_card_assignments", x => x.id);
                    table.CheckConstraint("ck_client_card_assignments_ended_after_assigned", "ended_at is null or ended_at >= assigned_at");
                    table.CheckConstraint("ck_client_card_assignments_lifecycle", "(\n    is_current\n    and ended_at is null\n    and ended_by_account_id is null\n    and end_reason is null\n)\nor (\n    not is_current\n    and ended_at is not null\n    and ended_by_account_id is not null\n    and end_reason is not null\n    and length(btrim(end_reason)) > 0\n)");
                    table.CheckConstraint("ck_client_card_assignments_normalized_not_empty", "length(btrim(card_number_normalized)) > 0");
                    table.CheckConstraint("ck_client_card_assignments_raw_not_empty", "length(btrim(card_number_raw)) > 0");
                    table.ForeignKey(
                        name: "FK_client_card_assignments_accounts_assigned_by_account_id",
                        column: x => x.assigned_by_account_id,
                        principalSchema: "bodylife",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_client_card_assignments_accounts_ended_by_account_id",
                        column: x => x.ended_by_account_id,
                        principalSchema: "bodylife",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_client_card_assignments_clients_client_id",
                        column: x => x.client_id,
                        principalSchema: "bodylife",
                        principalTable: "clients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_client_card_assignments_assigned_by_account_id",
                schema: "bodylife",
                table: "client_card_assignments",
                column: "assigned_by_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_client_card_assignments_client_history",
                schema: "bodylife",
                table: "client_card_assignments",
                columns: new[] { "client_id", "assigned_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_client_card_assignments_ended_by_account_id",
                schema: "bodylife",
                table: "client_card_assignments",
                column: "ended_by_account_id");

            migrationBuilder.CreateIndex(
                name: "ux_client_card_assignments_current_card",
                schema: "bodylife",
                table: "client_card_assignments",
                column: "card_number_normalized",
                unique: true,
                filter: "is_current");

            migrationBuilder.CreateIndex(
                name: "ux_client_card_assignments_current_client",
                schema: "bodylife",
                table: "client_card_assignments",
                column: "client_id",
                unique: true,
                filter: "is_current");

            migrationBuilder.CreateIndex(
                name: "ix_clients_created_by_account_id",
                schema: "bodylife",
                table: "clients",
                column: "created_by_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_clients_normalized_full_name",
                schema: "bodylife",
                table: "clients",
                column: "normalized_full_name");

            migrationBuilder.CreateIndex(
                name: "ix_clients_phone_last4_status",
                schema: "bodylife",
                table: "clients",
                columns: new[] { "phone_last4", "operational_status" },
                filter: "phone_last4 is not null");

            migrationBuilder.CreateIndex(
                name: "ix_clients_phone_normalized",
                schema: "bodylife",
                table: "clients",
                column: "phone_normalized",
                filter: "phone_normalized is not null");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "client_card_assignments",
                schema: "bodylife");

            migrationBuilder.DropTable(
                name: "clients",
                schema: "bodylife");
        }
    }
}
