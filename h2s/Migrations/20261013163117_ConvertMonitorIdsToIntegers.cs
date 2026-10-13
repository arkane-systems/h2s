using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace h2s.Migrations
{
    /// <inheritdoc />
    public partial class ConvertMonitorIdsToIntegers : Migration
    {
        // True when the text "MonitorId" holds a positive 32-bit integer, ignoring surrounding whitespace.
        private const string IsValidMonitorId =
            """trim("MonitorId") <> '' AND trim("MonitorId") NOT GLOB '*[^0-9]*' AND CAST(trim("MonitorId") AS INTEGER) BETWEEN 1 AND 2147483647""";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Monitor IDs were stored as text. The admin pages only ever accepted positive integers, but make sure:
            // clear an invalid optional link monitor, drop a status-bar group whose monitor is invalid, and rewrite
            // the rest in canonical form, so every value converts cleanly when the columns are rebuilt as INTEGER.
            migrationBuilder.Sql($"""UPDATE "Links" SET "MonitorId" = NULL WHERE "MonitorId" IS NOT NULL AND NOT ({IsValidMonitorId});""");
            migrationBuilder.Sql($"""DELETE FROM "InfrastructureGroups" WHERE "MonitorId" IS NULL OR NOT ({IsValidMonitorId});""");
            migrationBuilder.Sql("""UPDATE "Links" SET "MonitorId" = CAST(trim("MonitorId") AS INTEGER) WHERE "MonitorId" IS NOT NULL;""");
            migrationBuilder.Sql("""UPDATE "InfrastructureGroups" SET "MonitorId" = CAST(trim("MonitorId") AS INTEGER);""");

            migrationBuilder.AlterColumn<int>(
                name: "MonitorId",
                table: "Links",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "MonitorId",
                table: "InfrastructureGroups",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "TEXT");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "MonitorId",
                table: "Links",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "MonitorId",
                table: "InfrastructureGroups",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "INTEGER");
        }
    }
}
