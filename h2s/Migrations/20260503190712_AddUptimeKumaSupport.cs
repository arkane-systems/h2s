using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace h2s.Migrations
{
    /// <inheritdoc />
    public partial class AddUptimeKumaSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "UptimeKumaDefaultDuration",
                table: "DashboardSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "UptimeKumaServerUrl",
                table: "DashboardSettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UptimeKumaStatusPageSlug",
                table: "DashboardSettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "InfrastructureGroups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    MonitorId = table.Column<string>(type: "TEXT", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InfrastructureGroups", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InfrastructureGroups");

            migrationBuilder.DropColumn(
                name: "UptimeKumaDefaultDuration",
                table: "DashboardSettings");

            migrationBuilder.DropColumn(
                name: "UptimeKumaServerUrl",
                table: "DashboardSettings");

            migrationBuilder.DropColumn(
                name: "UptimeKumaStatusPageSlug",
                table: "DashboardSettings");
        }
    }
}
