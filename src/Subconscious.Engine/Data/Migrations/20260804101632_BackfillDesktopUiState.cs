using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Subconscious.Engine.Data.Migrations
{
    /// <inheritdoc />
    public partial class BackfillDesktopUiState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Python UI rows predate client scoping. Prefer an already-written desktop value on
            // conflict, then place the remaining legacy state in the Desktop client scope.
            migrationBuilder.Sql("DELETE FROM app_state WHERE tag = 'ui_state' AND client IS NULL AND EXISTS (SELECT 1 FROM app_state scoped WHERE scoped.key = app_state.key AND scoped.tag = app_state.tag AND scoped.client = 'desktop');");
            migrationBuilder.Sql("UPDATE app_state SET client = 'desktop' WHERE tag = 'ui_state' AND client IS NULL;");

            // Earlier Desktop builds used UUID state keys. Preserve their selections under the
            // Python-compatible numeric-ID keys before dropping the divergent aliases.
            migrationBuilder.Sql("INSERT INTO app_state (key, value, tag, client) SELECT 'ui_active_workspace_id', CAST(workspaces.id AS TEXT), legacy.tag, legacy.client FROM app_state legacy JOIN workspaces ON workspaces.uuid = legacy.value WHERE legacy.key = 'ui_active_workspace_uuid' AND legacy.tag = 'ui_state' AND legacy.client = 'desktop' AND NOT EXISTS (SELECT 1 FROM app_state canonical WHERE canonical.key = 'ui_active_workspace_id' AND canonical.tag = legacy.tag AND canonical.client = legacy.client);");
            migrationBuilder.Sql("INSERT INTO app_state (key, value, tag, client) SELECT 'ui_selected_workspace_id', CAST(workspaces.id AS TEXT), legacy.tag, legacy.client FROM app_state legacy JOIN workspaces ON workspaces.uuid = legacy.value WHERE legacy.key = 'ui_selected_workspace_uuid' AND legacy.tag = 'ui_state' AND legacy.client = 'desktop' AND NOT EXISTS (SELECT 1 FROM app_state canonical WHERE canonical.key = 'ui_selected_workspace_id' AND canonical.tag = legacy.tag AND canonical.client = legacy.client);");
            migrationBuilder.Sql("INSERT INTO app_state (key, value, tag, client) SELECT 'ui_selected_thread_id', CAST(threads.id AS TEXT), legacy.tag, legacy.client FROM app_state legacy JOIN threads ON threads.uuid = legacy.value WHERE legacy.key = 'ui_selected_thread_uuid' AND legacy.tag = 'ui_state' AND legacy.client = 'desktop' AND NOT EXISTS (SELECT 1 FROM app_state canonical WHERE canonical.key = 'ui_selected_thread_id' AND canonical.tag = legacy.tag AND canonical.client = legacy.client);");
            migrationBuilder.Sql("DELETE FROM app_state WHERE tag = 'ui_state' AND client = 'desktop' AND key IN ('ui_active_workspace_uuid', 'ui_selected_workspace_uuid', 'ui_selected_thread_uuid');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE app_state SET client = NULL WHERE tag = 'ui_state' AND client = 'desktop';");
        }
    }
}
