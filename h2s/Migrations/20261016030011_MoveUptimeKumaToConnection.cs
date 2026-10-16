using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace h2s.Migrations
{
    /// <inheritdoc />
    public partial class MoveUptimeKumaToConnection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Connections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    IntegrationKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    OptionsJson = table.Column<string>(type: "TEXT", nullable: false),
                    SecretsJson = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Connections", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Connections_IntegrationKey_Name",
                table: "Connections",
                columns: new[] { "IntegrationKey", "Name" },
                unique: true);

            // Uptime Kuma was on when it had a server URL; it becomes the Uptime Kuma connection. Literal keys, since the
            // integration's constants may change but this migration's data mustn't. A duration of 0 or less read as 24.
            migrationBuilder.Sql("""
                INSERT INTO "Connections" ("IntegrationKey", "Name", "OptionsJson")
                SELECT 'uptime-kuma', 'Uptime Kuma',
                       json_object('serverUrl', trim("UptimeKumaServerUrl"),
                                   'statusPageSlug', trim(coalesce("UptimeKumaStatusPageSlug", '')),
                                   'defaultDuration', CASE WHEN "UptimeKumaDefaultDuration" > 0
                                                           THEN "UptimeKumaDefaultDuration" ELSE 24 END)
                FROM "DashboardSettings"
                WHERE "Id" = 1 AND trim(coalesce("UptimeKumaServerUrl", '')) <> '';
                """);

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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "UptimeKumaDefaultDuration",
                table: "DashboardSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 24);

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

            // Put the Uptime Kuma connection's settings back.
            migrationBuilder.Sql("""
                UPDATE "DashboardSettings"
                SET "UptimeKumaServerUrl" = (SELECT nullif(trim(json_extract("OptionsJson", '$.serverUrl')), '')
                                             FROM "Connections" WHERE "IntegrationKey" = 'uptime-kuma' ORDER BY "Id" LIMIT 1),
                    "UptimeKumaStatusPageSlug" = (SELECT nullif(trim(json_extract("OptionsJson", '$.statusPageSlug')), '')
                                                  FROM "Connections" WHERE "IntegrationKey" = 'uptime-kuma' ORDER BY "Id" LIMIT 1),
                    "UptimeKumaDefaultDuration" = coalesce((SELECT json_extract("OptionsJson", '$.defaultDuration')
                                                            FROM "Connections" WHERE "IntegrationKey" = 'uptime-kuma'
                                                            ORDER BY "Id" LIMIT 1), 24)
                WHERE "Id" = 1;
                """);

            migrationBuilder.DropTable(
                name: "Connections");
        }
    }
}
