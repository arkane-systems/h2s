using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace h2s.Migrations
{
    /// <inheritdoc />
    public partial class ConvertGroupsToStatusBarWidgets : Migration
    {
        // AddPanelWidgets, just before, gave widgets their panel and label: a migration of its own, since SQLite rebuilds a
        // changed table only at the end of the migration that changes it.

        // The first status bar, and the Uptime Kuma connection (widgets use the integration's first). Literal keys
        // throughout, since the code's constants may change but this migration's data mustn't.
        private const string FirstStatusBar = """
            (SELECT min("Id") FROM "Panels" WHERE "Kind" = 'status-bar')
            """;

        private const string KumaConnection = """
            (SELECT min("Id") FROM "Connections" WHERE "IntegrationKey" = 'uptime-kuma')
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Each infrastructure group becomes an Uptime Kuma monitor widget on the first status bar, labelled with its
            // name: no response time, and uptime as the bar showed it, over the bar's window (blank follows the
            // connection). Without a status bar or a Kuma connection the groups weren't shown, and go.
            migrationBuilder.Sql($"""
                INSERT INTO "Widgets" ("PanelId", "LinkId", "ConnectionId", "Kind", "OptionsJson", "RefreshSeconds", "Label", "SortOrder")
                SELECT p."Id", NULL, {KumaConnection}, 'monitor',
                       json_object('monitorId', g."MonitorId",
                                   'showResponseTime', json('false'),
                                   'showUptime', CASE WHEN coalesce(CASE WHEN json_valid(p."OptionsJson")
                                                                         THEN json_extract(p."OptionsJson", '$.showUptime') END, 1)
                                                      THEN json('true') ELSE json('false') END,
                                   'uptimeWindow', coalesce(CASE WHEN json_valid(p."OptionsJson")
                                                                 THEN json_extract(p."OptionsJson", '$.uptimeWindow') END, '')),
                       NULL, substr(trim(g."Name"), 1, 100), g."SortOrder"
                FROM "InfrastructureGroups" AS g
                JOIN "Panels" AS p ON p."Id" = {FirstStatusBar}
                WHERE g."MonitorId" > 0 AND {KumaConnection} IS NOT NULL;
                """);

            // The bars' uptime options moved into each item. The Kuma status page the bar linked to becomes the first bar's
            // link, and the connection forgets it.
            migrationBuilder.Sql("""
                UPDATE "Panels"
                SET "OptionsJson" = json_remove("OptionsJson", '$.showUptime', '$.uptimeWindow', '$.uptimeHours')
                WHERE "Kind" = 'status-bar' AND json_valid("OptionsJson");
                """);

            migrationBuilder.Sql($"""
                UPDATE "Panels"
                SET "OptionsJson" = json_set("OptionsJson", '$.linkUrl', (
                    SELECT rtrim(trim(json_extract(c."OptionsJson", '$.serverUrl')), '/') || '/status/' ||
                           trim(json_extract(c."OptionsJson", '$.statusPageSlug'))
                    FROM "Connections" AS c WHERE c."Id" = {KumaConnection}))
                WHERE "Id" = {FirstStatusBar} AND json_valid("OptionsJson")
                  AND EXISTS (
                      SELECT 1 FROM "Connections" AS c
                      WHERE c."Id" = {KumaConnection} AND json_valid(c."OptionsJson")
                        AND trim(coalesce(json_extract(c."OptionsJson", '$.serverUrl'), '')) <> ''
                        AND trim(coalesce(json_extract(c."OptionsJson", '$.statusPageSlug'), '')) <> '');
                """);

            migrationBuilder.Sql("""
                UPDATE "Connections"
                SET "OptionsJson" = json_remove("OptionsJson", '$.statusPageSlug')
                WHERE "IntegrationKey" = 'uptime-kuma' AND json_valid("OptionsJson");
                """);

            migrationBuilder.DropTable(
                name: "InfrastructureGroups");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InfrastructureGroups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MonitorId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InfrastructureGroups", x => x.Id);
                });

            // The first bar's Kuma monitor widgets become groups again, and its link the Kuma status page when it points
            // at one. Other bars' and other integrations' items can't be groups, and go with the rest of the panels'
            // widgets; so do the extra bars, since there was only ever one.
            migrationBuilder.Sql($"""
                INSERT INTO "InfrastructureGroups" ("Name", "MonitorId", "SortOrder")
                SELECT coalesce(nullif(trim(w."Label"), ''), 'Monitor ' || json_extract(w."OptionsJson", '$.monitorId')),
                       CAST(json_extract(w."OptionsJson", '$.monitorId') AS INTEGER), w."SortOrder"
                FROM "Widgets" AS w
                JOIN "Connections" AS c ON c."Id" = w."ConnectionId"
                WHERE w."PanelId" = {FirstStatusBar} AND c."IntegrationKey" = 'uptime-kuma' AND w."Kind" = 'monitor'
                  AND json_valid(w."OptionsJson") AND CAST(json_extract(w."OptionsJson", '$.monitorId') AS INTEGER) > 0;
                """);

            migrationBuilder.Sql($"""
                UPDATE "Connections"
                SET "OptionsJson" = json_set("OptionsJson", '$.statusPageSlug', substr(
                    (SELECT json_extract(p."OptionsJson", '$.linkUrl') FROM "Panels" AS p WHERE p."Id" = {FirstStatusBar}),
                    length(rtrim(trim(json_extract("OptionsJson", '$.serverUrl')), '/') || '/status/') + 1))
                WHERE "Id" = {KumaConnection} AND json_valid("OptionsJson")
                  AND (SELECT json_extract(p."OptionsJson", '$.linkUrl') FROM "Panels" AS p WHERE p."Id" = {FirstStatusBar})
                      LIKE rtrim(trim(json_extract("OptionsJson", '$.serverUrl')), '/') || '/status/_%';
                """);

            migrationBuilder.Sql($"""
                DELETE FROM "Widgets" WHERE "PanelId" IS NOT NULL;
                DELETE FROM "Panels" WHERE "Kind" = 'status-bar' AND "Id" <> {FirstStatusBar};
                UPDATE "Panels" SET "OptionsJson" = json_remove("OptionsJson", '$.title', '$.linkUrl')
                WHERE "Kind" = 'status-bar' AND json_valid("OptionsJson");
                """);
        }
    }
}
