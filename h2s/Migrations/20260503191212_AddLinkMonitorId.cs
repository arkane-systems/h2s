using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace h2s.Migrations
{
    /// <inheritdoc />
    public partial class AddLinkMonitorId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MonitorId",
                table: "Links",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MonitorId",
                table: "Links");
        }
    }
}
