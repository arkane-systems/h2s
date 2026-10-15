using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace h2s.Migrations
{
    /// <inheritdoc />
    public partial class AddCategoryAndLinkSortOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                table: "Links",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                table: "Categories",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            // Start from the order the dashboard showed before: categories by name within the regular and admin
            // sections (the database's binary collation, as EF Core sorted them), and links by label within each
            // category (case-insensitively, as the page sorted them in memory). Ids break any remaining ties.
            migrationBuilder.Sql("""
                UPDATE "Categories" SET "SortOrder" = (
                    SELECT "Position" FROM (
                        SELECT "Id", ROW_NUMBER() OVER (PARTITION BY "IsAdminCategory" ORDER BY "Name", "Id") - 1 AS "Position"
                        FROM "Categories") AS "Ordered"
                    WHERE "Ordered"."Id" = "Categories"."Id");
                """);
            migrationBuilder.Sql("""
                UPDATE "Links" SET "SortOrder" = (
                    SELECT "Position" FROM (
                        SELECT "Id", ROW_NUMBER() OVER (PARTITION BY "CategoryId" ORDER BY "Label" COLLATE NOCASE, "Label", "Id") - 1 AS "Position"
                        FROM "Links") AS "Ordered"
                    WHERE "Ordered"."Id" = "Links"."Id");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SortOrder",
                table: "Links");

            migrationBuilder.DropColumn(
                name: "SortOrder",
                table: "Categories");
        }
    }
}
