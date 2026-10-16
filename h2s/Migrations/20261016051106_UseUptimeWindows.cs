using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace h2s.Migrations
{
    /// <inheritdoc />
    public partial class UseUptimeWindows : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Uptime periods become Uptime Kuma's windows (1d, 30d, 365d): each period in hours becomes the shortest window
            // that covers it. Literal keys, since the code's constants may change but this migration's data mustn't. A
            // status bar with no period of its own keeps none, and follows the connection's default.
            migrationBuilder.Sql("""
                UPDATE "Connections"
                SET "OptionsJson" = json_remove(
                        json_set("OptionsJson", '$.defaultUptimeWindow',
                                 CASE WHEN coalesce(json_extract("OptionsJson", '$.defaultDuration'), 24) <= 24 THEN '1d'
                                      WHEN json_extract("OptionsJson", '$.defaultDuration') <= 720 THEN '30d'
                                      ELSE '365d' END),
                        '$.defaultDuration')
                WHERE "IntegrationKey" = 'uptime-kuma' AND json_valid("OptionsJson");
                """);

            migrationBuilder.Sql("""
                UPDATE "Panels"
                SET "OptionsJson" = CASE
                        WHEN json_type("OptionsJson", '$.uptimeHours') = 'integer' THEN json_remove(
                            json_set("OptionsJson", '$.uptimeWindow',
                                     CASE WHEN json_extract("OptionsJson", '$.uptimeHours') <= 24 THEN '1d'
                                          WHEN json_extract("OptionsJson", '$.uptimeHours') <= 720 THEN '30d'
                                          ELSE '365d' END),
                            '$.uptimeHours')
                        ELSE json_remove("OptionsJson", '$.uptimeHours') END
                WHERE "Kind" = 'status-bar' AND json_valid("OptionsJson");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE "Connections"
                SET "OptionsJson" = json_remove(
                        json_set("OptionsJson", '$.defaultDuration',
                                 CASE json_extract("OptionsJson", '$.defaultUptimeWindow')
                                      WHEN '30d' THEN 720 WHEN '365d' THEN 8760 ELSE 24 END),
                        '$.defaultUptimeWindow')
                WHERE "IntegrationKey" = 'uptime-kuma' AND json_valid("OptionsJson");
                """);

            migrationBuilder.Sql("""
                UPDATE "Panels"
                SET "OptionsJson" = CASE
                        WHEN json_extract("OptionsJson", '$.uptimeWindow') IN ('1d', '30d', '365d') THEN json_remove(
                            json_set("OptionsJson", '$.uptimeHours',
                                     CASE json_extract("OptionsJson", '$.uptimeWindow')
                                          WHEN '30d' THEN 720 WHEN '365d' THEN 8760 ELSE 24 END),
                            '$.uptimeWindow')
                        ELSE json_remove("OptionsJson", '$.uptimeWindow') END
                WHERE "Kind" = 'status-bar' AND json_valid("OptionsJson");
                """);
        }
    }
}
