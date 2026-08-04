using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Subconscious.Engine.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAppStateClient : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "uq_app_state_key_tag",
                table: "app_state");

            migrationBuilder.AddColumn<string>(
                name: "client",
                table: "app_state",
                type: "TEXT",
                nullable: true);

            // Preserve the configuration introduced before client-scoped state existed.
            migrationBuilder.Sql("UPDATE app_state SET client = 'desktop' WHERE key = 'panel_configuration' AND tag = 'ui_state' AND client IS NULL;");

            migrationBuilder.CreateIndex(
                name: "uq_app_state_key_tag_client",
                table: "app_state",
                columns: new[] { "key", "tag", "client" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "uq_app_state_key_tag_client",
                table: "app_state");

            migrationBuilder.DropColumn(
                name: "client",
                table: "app_state");

            migrationBuilder.CreateIndex(
                name: "uq_app_state_key_tag",
                table: "app_state",
                columns: new[] { "key", "tag" },
                unique: true);
        }
    }
}
