using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace h2s.Migrations
{
    /// <inheritdoc />
    public partial class AddPanelWidgets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "LinkId",
                table: "Widgets",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.AddColumn<string>(
                name: "Label",
                table: "Widgets",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PanelId",
                table: "Widgets",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Widgets_PanelId",
                table: "Widgets",
                column: "PanelId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Widgets_OneOwner",
                table: "Widgets",
                sql: "(\"LinkId\" IS NULL) <> (\"PanelId\" IS NULL)");

            migrationBuilder.AddForeignKey(
                name: "FK_Widgets_Panels_PanelId",
                table: "Widgets",
                column: "PanelId",
                principalTable: "Panels",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Widgets_Panels_PanelId",
                table: "Widgets");

            migrationBuilder.DropIndex(
                name: "IX_Widgets_PanelId",
                table: "Widgets");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Widgets_OneOwner",
                table: "Widgets");

            migrationBuilder.DropColumn(
                name: "Label",
                table: "Widgets");

            migrationBuilder.DropColumn(
                name: "PanelId",
                table: "Widgets");

            migrationBuilder.AlterColumn<int>(
                name: "LinkId",
                table: "Widgets",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);
        }
    }
}
