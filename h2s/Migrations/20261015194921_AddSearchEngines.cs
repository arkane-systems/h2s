using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace h2s.Migrations
{
    /// <inheritdoc />
    public partial class AddSearchEngines : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SearchEngines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    UrlTemplate = table.Column<string>(type: "TEXT", nullable: false),
                    Shortcut = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    IsDefault = table.Column<bool>(type: "INTEGER", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SearchEngines", x => x.Id);
                });

            // Bing stays the default, as the search box always used it, so upgrading changes nothing visible.
            migrationBuilder.InsertData(
                table: "SearchEngines",
                columns: new[] { "Name", "UrlTemplate", "Shortcut", "IsDefault", "SortOrder" },
                values: new object[,]
                {
                    { "Bing", "https://www.bing.com/search?q={query}", "b", true, 0 },
                    { "DuckDuckGo", "https://duckduckgo.com/?q={query}", "d", false, 1 },
                    { "Google", "https://www.google.com/search?q={query}", "g", false, 2 },
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SearchEngines");
        }
    }
}
