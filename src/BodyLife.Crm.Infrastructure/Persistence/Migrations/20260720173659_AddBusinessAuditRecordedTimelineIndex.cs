using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BodyLife.Crm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBusinessAuditRecordedTimelineIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_business_audit_entries_recorded_timeline",
                schema: "bodylife",
                table: "business_audit_entries",
                columns: new[] { "recorded_at", "id" },
                descending: new bool[0]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_business_audit_entries_recorded_timeline",
                schema: "bodylife",
                table: "business_audit_entries");
        }
    }
}
