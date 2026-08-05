using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging;

namespace Subconscious.Engine.Data;

/// <summary>
/// Brings the database up to date using real EF Core migrations, while staying compatible with
/// every database that predates migrations entirely — every file created by
/// <c>EnsureCreatedAsync</c> in earlier .NET engine builds, and every file created by the
/// original Python/SQLAlchemy engine (see <c>db/session.py</c>'s <c>init_models</c> /
/// <c>ALTER TABLE</c> dance, which this migration history now supersedes).
///
/// The problem <see cref="MigrateAsync"/> solves: <c>Database.MigrateAsync()</c> assumes a
/// database is either brand new or was itself created by a previous <c>MigrateAsync()</c> call,
/// i.e. it has a <c>__EFMigrationsHistory</c> row per applied migration. Neither is true for an
/// existing file here — it already has some or all of the mapped tables, created ad hoc, with
/// no history table at all. Running the <c>InitialCreate</c> migration against it verbatim
/// would fail on "table already exists" at the very first <c>CREATE TABLE</c>.
///
/// The fix: detect that case once (mapped tables present, no history table), reconcile the file
/// additively up to the current model with <see cref="SqliteSchemaReconciler"/> — safe, because
/// it only ever adds tables/columns, never drops, renames or retypes anything — then stamp every
/// currently-defined migration into the history table without re-running its SQL, since the
/// reconciler already made the schema match what those migrations would have produced. From
/// that point on the file is an ordinary, fully migrated database, and every future migration is
/// applied the normal way.
/// </summary>
public static class DatabaseMigrator
{
    public static async Task MigrateAsync(
        SubconsciousDbContext db,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        if (!await HistoryTableExistsAsync(db, cancellationToken)
            && await HasAnyMappedTableAsync(db, cancellationToken))
        {
            logger?.LogInformation(
                "Schema check: found an existing database with no migration history. Baselining it in place before applying migrations.");

            var applied = await SqliteSchemaReconciler.ReconcileAsync(db, logger, cancellationToken);
            if (applied.Count > 0)
            {
                logger?.LogInformation("Schema check: applied {Count} additive change(s) while baselining.", applied.Count);
            }

            await BackfillLegacyDesktopUiStateAsync(db, cancellationToken);
            await StampAllMigrationsAsync(db, cancellationToken);
        }

        var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        if (pending.Count > 0)
        {
            logger?.LogInformation("Schema check: applying {Count} pending migration(s): {Migrations}.", pending.Count, string.Join(", ", pending));
        }

        await db.Database.MigrateAsync(cancellationToken);
    }

    private static async Task<bool> HistoryTableExistsAsync(SubconsciousDbContext db, CancellationToken cancellationToken)
    {
        await EnsureConnectionOpenAsync(db, cancellationToken);

        var connection = db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name = '__EFMigrationsHistory'";
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    /// <summary>Whether any table the current model maps to already exists in the file.</summary>
    private static async Task<bool> HasAnyMappedTableAsync(SubconsciousDbContext db, CancellationToken cancellationToken)
    {
        await EnsureConnectionOpenAsync(db, cancellationToken);

        var tables = db.Model.GetEntityTypes()
            .Where(e => !e.IsOwned())
            .Select(e => e.GetTableName())
            .Where(t => !string.IsNullOrEmpty(t))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        var connection = db.Database.GetDbConnection();
        foreach (var table in tables)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name = @name";
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@name";
            parameter.Value = table!;
            command.Parameters.Add(parameter);

            if (await command.ExecuteScalarAsync(cancellationToken) is not null)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Creates <c>__EFMigrationsHistory</c> and inserts a row for every migration defined in the
    /// assembly, without executing any of their <c>Up()</c> SQL. Only valid to call once the
    /// schema has already been brought in line with the model by other means (the reconciler).
    /// </summary>
    private static async Task StampAllMigrationsAsync(SubconsciousDbContext db, CancellationToken cancellationToken)
    {
        var historyRepository = db.GetService<IHistoryRepository>();

        await db.Database.ExecuteSqlRawAsync(historyRepository.GetCreateIfNotExistsScript(), cancellationToken);

        var productVersion = ProductInfo.GetVersion();
        foreach (var migrationId in db.Database.GetMigrations())
        {
            var insertScript = historyRepository.GetInsertScript(new HistoryRow(migrationId, productVersion));
            await db.Database.ExecuteSqlRawAsync(insertScript, cancellationToken);
        }
    }

    /// <summary>Runs data compatibility work skipped when a legacy Python database is baselined
    /// instead of replaying migrations over its existing tables.</summary>
    private static async Task BackfillLegacyDesktopUiStateAsync(SubconsciousDbContext db, CancellationToken cancellationToken)
    {
        var statements = new[]
        {
            "DELETE FROM app_state WHERE tag = 'ui_state' AND client IS NULL AND EXISTS (SELECT 1 FROM app_state scoped WHERE scoped.key = app_state.key AND scoped.tag = app_state.tag AND scoped.client = 'desktop');",
            "UPDATE app_state SET client = 'desktop' WHERE tag = 'ui_state' AND client IS NULL;",
            "INSERT INTO app_state (key, value, tag, client) SELECT 'ui_active_workspace_id', CAST(workspaces.id AS TEXT), legacy.tag, legacy.client FROM app_state legacy JOIN workspaces ON workspaces.uuid = legacy.value WHERE legacy.key = 'ui_active_workspace_uuid' AND legacy.tag = 'ui_state' AND legacy.client = 'desktop' AND NOT EXISTS (SELECT 1 FROM app_state canonical WHERE canonical.key = 'ui_active_workspace_id' AND canonical.tag = legacy.tag AND canonical.client = legacy.client);",
            "INSERT INTO app_state (key, value, tag, client) SELECT 'ui_selected_workspace_id', CAST(workspaces.id AS TEXT), legacy.tag, legacy.client FROM app_state legacy JOIN workspaces ON workspaces.uuid = legacy.value WHERE legacy.key = 'ui_selected_workspace_uuid' AND legacy.tag = 'ui_state' AND legacy.client = 'desktop' AND NOT EXISTS (SELECT 1 FROM app_state canonical WHERE canonical.key = 'ui_selected_workspace_id' AND canonical.tag = legacy.tag AND canonical.client = legacy.client);",
            "INSERT INTO app_state (key, value, tag, client) SELECT 'ui_selected_thread_id', CAST(threads.id AS TEXT), legacy.tag, legacy.client FROM app_state legacy JOIN threads ON threads.uuid = legacy.value WHERE legacy.key = 'ui_selected_thread_uuid' AND legacy.tag = 'ui_state' AND legacy.client = 'desktop' AND NOT EXISTS (SELECT 1 FROM app_state canonical WHERE canonical.key = 'ui_selected_thread_id' AND canonical.tag = legacy.tag AND canonical.client = legacy.client);",
            "DELETE FROM app_state WHERE tag = 'ui_state' AND client = 'desktop' AND key IN ('ui_active_workspace_uuid', 'ui_selected_workspace_uuid', 'ui_selected_thread_uuid');",
        };

        foreach (var statement in statements)
        {
            await db.Database.ExecuteSqlRawAsync(statement, cancellationToken);
        }
    }

    private static async Task EnsureConnectionOpenAsync(SubconsciousDbContext db, CancellationToken cancellationToken)
    {
        if (db.Database.GetDbConnection().State != System.Data.ConnectionState.Open)
        {
            await db.Database.OpenConnectionAsync(cancellationToken);
        }
    }
}
