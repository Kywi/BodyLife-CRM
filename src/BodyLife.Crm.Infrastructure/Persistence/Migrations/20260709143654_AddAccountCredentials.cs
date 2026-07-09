using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BodyLife.Crm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountCredentials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "account_credentials",
                schema: "bodylife",
                columns: table => new
                {
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    login_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    normalized_login_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    password_hash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    password_changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_account_credentials", x => x.account_id);
                    table.CheckConstraint("ck_account_credentials_login_name_not_empty", "length(btrim(login_name)) > 0");
                    table.CheckConstraint("ck_account_credentials_normalized_login_name_not_empty", "length(btrim(normalized_login_name)) > 0");
                    table.CheckConstraint("ck_account_credentials_password_hash_not_empty", "length(btrim(password_hash)) > 0");
                    table.ForeignKey(
                        name: "FK_account_credentials_accounts_account_id",
                        column: x => x.account_id,
                        principalSchema: "bodylife",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_account_credentials_normalized_login_name",
                schema: "bodylife",
                table: "account_credentials",
                column: "normalized_login_name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "account_credentials",
                schema: "bodylife");
        }
    }
}
