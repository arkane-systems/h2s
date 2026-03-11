using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace h2s.Migrations
{
    /// <inheritdoc />
    public partial class AddDashboardSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DashboardSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Title = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DashboardSettings", x => x.Id);
                    table.CheckConstraint("CK_DashboardSettings_SingleRow", "Id = 1");
                });

            // Seed the default singleton record
            migrationBuilder.InsertData(
                table: "DashboardSettings",
                columns: new[] { "Id", "Title" },
                values: new object[] { 1, "Dashboard" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DashboardSettings");
        }
    }
}
