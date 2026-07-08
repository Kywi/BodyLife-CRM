using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BodyLife.Crm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialBaseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "bodylife");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropSchema(
                name: "bodylife");
        }
    }
}
