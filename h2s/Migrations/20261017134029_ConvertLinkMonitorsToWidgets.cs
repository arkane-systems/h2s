using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace h2s.Migrations
{
    /// <inheritdoc />
    public partial class ConvertLinkMonitorsToWidgets : Migration
    {
        // The Uptime Kuma connection, if there is one: widgets use the integration's first connection.
        private const string KumaConnection = """
            (SELECT min("Id") FROM "Connections" WHERE "IntegrationKey" = 'uptime-kuma')
            """;

        // The links whose monitor becomes a widget: a positive monitor ID, an Uptime Kuma connection, fewer than the ten
        // widgets a link may have, and no monitor widget for that monitor already (someone may have added one by hand
        // since widgets arrived), its ID stored as a number or as text.
        private const string Converted = $$"""
            SELECT "Id" FROM "Links" AS l
            WHERE l."MonitorId" > 0
              AND {{KumaConnection}} IS NOT NULL
              AND (SELECT count(*) FROM "Widgets" AS w WHERE w."LinkId" = l."Id") < 10
              AND NOT EXISTS (
                  SELECT 1 FROM "Widgets" AS w
                  JOIN "Connections" AS c ON c."Id" = w."ConnectionId"
                  WHERE w."LinkId" = l."Id"
                    AND c."IntegrationKey" = 'uptime-kuma'
                    AND w."Kind" = 'monitor'
                    AND json_valid(w."OptionsJson")
                    AND CAST(json_extract(w."OptionsJson", '$.monitorId') AS INTEGER) = l."MonitorId")
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Each link's monitor becomes an Uptime Kuma monitor widget, first among its widgets, with the kind's defaults:
            // status, response time, and uptime over the connection's default window. Literal keys, since the code's
            // constants may change but this migration's data mustn't. Without a connection the monitor IDs are dropped,
            // as their badges weren't shown either.
            migrationBuilder.Sql($"""
                UPDATE "Widgets" SET "SortOrder" = "SortOrder" + 1
                WHERE "LinkId" IN ({Converted});
                """);

            migrationBuilder.Sql($"""
                INSERT INTO "Widgets" ("LinkId", "ConnectionId", "Kind", "OptionsJson", "RefreshSeconds", "SortOrder")
                SELECT l."Id", {KumaConnection}, 'monitor',
                       json_object('monitorId', l."MonitorId", 'showResponseTime', json('true'), 'showUptime', json('true'),
                                   'uptimeWindow', ''),
                       NULL, 0
                FROM "Links" AS l
                WHERE l."Id" IN ({Converted});
                """);

            migrationBuilder.DropColumn(
                name: "MonitorId",
                table: "Links");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MonitorId",
                table: "Links",
                type: "INTEGER",
                nullable: true);

            // Each link gets back the monitor of its first Uptime Kuma monitor widget. The widgets stay, since the ones this
            // migration added can't be told from the rest.
            migrationBuilder.Sql("""
                UPDATE "Links" SET "MonitorId" = (
                    SELECT CAST(json_extract(w."OptionsJson", '$.monitorId') AS INTEGER)
                    FROM "Widgets" AS w
                    JOIN "Connections" AS c ON c."Id" = w."ConnectionId"
                    WHERE w."LinkId" = "Links"."Id"
                      AND c."IntegrationKey" = 'uptime-kuma'
                      AND w."Kind" = 'monitor'
                      AND json_valid(w."OptionsJson")
                    ORDER BY w."SortOrder", w."Id"
                    LIMIT 1);
                """);
        }
    }
}
