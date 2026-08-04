using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Logging;

namespace Subconscious.Engine.Data;

/// <summary>
/// Brings an existing SQLite file up to date with the current EF model, additively.
///
/// Why this exists: the engine bootstraps with <c>EnsureCreatedAsync</c>, which creates the whole
/// schema when the file is absent and does <em>nothing at all</em> when it is present. So every
/// entity property added after a user's <c>subconscious.db</c> was first created is missing from
/// their file forever, and the first query that mentions it fails with
/// <c>SQLite Error 1: 'no such column: …'</c> — surfacing as a 500 from the API rather than
/// anything that points at the schema. That is exactly what
/// <c>GET /api/v1/workspaces</c> was doing: the <c>workspaces</c> table predates
/// <c>default_model_id</c>.
///
/// What it does: creates mapped tables/indexes that don't exist, and adds mapped columns that
/// don't exist. Nothing is ever dropped, renamed, retyped or reordered, and no rows are touched,
/// so the worst case is a column that stays unused. It is a stopgap, not a migration system —
/// renames, type changes and backfills all still need real EF Core migrations.
/// </summary>
public static class SqliteSchemaReconciler
{
    /// <summary>
    /// Adds anything the model has and the file doesn't. Returns the DDL it ran, in order, so the
    /// caller can log it (an empty list means the file was already current).
    /// </summary>
    public static async Task<IReadOnlyList<string>> ReconcileAsync(
        SubconsciousDbContext db,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        var applied = new List<string>();

        var tables = db.Model.GetEntityTypes()
            .Where(e => !e.IsOwned())
            .Select(e => (Entity: e, Table: e.GetTableName()))
            .Where(x => !string.IsNullOrEmpty(x.Table))
            .GroupBy(x => x.Table!, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var missingTables = new List<string>();
        foreach (var group in tables)
        {
            if ((await GetColumnsAsync(db, group.Key, cancellationToken)).Count == 0)
            {
                missingTables.Add(group.Key);
            }
        }

        if (missingTables.Count > 0)
        {
            logger?.LogInformation(
                "Schema check: {Count} mapped table(s) missing from the database ({Tables}); creating them.",
                missingTables.Count,
                string.Join(", ", missingTables));

            applied.AddRange(await CreateMissingObjectsAsync(db, cancellationToken));
        }

        foreach (var group in tables)
        {
            applied.AddRange(await AddMissingColumnsAsync(db, group.Key, group.Select(x => x.Entity), logger, cancellationToken));
        }

        if (applied.Count > 0)
        {
            logger?.LogInformation("Schema check: applied {Count} additive change(s).", applied.Count);
        }

        return applied;
    }

    /// <summary>
    /// Replays the model's full create script, skipping every statement whose object already
    /// exists. SQLite reports that as a plain error rather than a no-op, and EF's generated script
    /// has no <c>IF NOT EXISTS</c>, so "already exists" is treated as success here. Only
    /// <c>CREATE</c> statements are ever run, so skipping cannot lose data.
    /// </summary>
    private static async Task<List<string>> CreateMissingObjectsAsync(
        SubconsciousDbContext db,
        CancellationToken cancellationToken)
    {
        var applied = new List<string>();

        foreach (var statement in SplitStatements(db.Database.GenerateCreateScript()))
        {
            try
            {
                await db.Database.ExecuteSqlRawAsync(statement, cancellationToken);
                applied.Add(statement);
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 1 && ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase))
            {
                // Object is already there — expected for every table that wasn't missing.
            }
        }

        return applied;
    }

    private static async Task<List<string>> AddMissingColumnsAsync(
        SubconsciousDbContext db,
        string table,
        IEnumerable<IEntityType> entities,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        var applied = new List<string>();

        var existing = await GetColumnsAsync(db, table, cancellationToken);
        if (existing.Count == 0)
        {
            // Table still absent after the create pass — the model maps something SQLite refused
            // to create. Leave it alone; ALTER TABLE would only produce a second, noisier error.
            return applied;
        }

        var storeObject = StoreObjectIdentifier.Table(table);

        foreach (var entity in entities)
        {
            foreach (var property in entity.GetProperties())
            {
                var column = property.GetColumnName(storeObject);
                if (string.IsNullOrEmpty(column) || existing.Contains(column))
                {
                    continue;
                }

                var type = property.GetColumnType(storeObject);
                if (string.IsNullOrEmpty(type))
                {
                    continue;
                }

                if (!property.IsColumnNullable(storeObject))
                {
                    // ALTER TABLE ADD COLUMN can only add a NOT NULL column with a constant
                    // default, and inventing one would write fabricated values into the user's
                    // rows. Say so loudly instead and leave the file untouched.
                    logger?.LogWarning(
                        "Schema check: {Table}.{Column} ({Type}) is required by the model but missing from the "
                        + "database, and cannot be added without fabricating a value for existing rows. "
                        + "Queries touching it will fail until this is migrated by hand.",
                        table, column, type);
                    continue;
                }

                var ddl = $"ALTER TABLE \"{table}\" ADD COLUMN \"{column}\" {type} NULL";
                await db.Database.ExecuteSqlRawAsync(ddl, cancellationToken);
                applied.Add(ddl);

                logger?.LogInformation("Schema check: added missing column {Table}.{Column} ({Type}).", table, column, type);

                existing.Add(column);
            }
        }

        return applied;
    }

    /// <summary>Column names of <paramref name="table"/>, or an empty set when it doesn't exist.</summary>
    private static async Task<HashSet<string>> GetColumnsAsync(
        SubconsciousDbContext db,
        string table,
        CancellationToken cancellationToken)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await db.Database.OpenConnectionAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        // PRAGMA takes no parameters, so the table name is quoted rather than bound. Every value
        // reaching here is a table name from the compiled EF model, never user input.
        command.CommandText = $"PRAGMA table_info(\"{table.Replace("\"", "\"\"")}\")";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            columns.Add(reader.GetString(1));
        }

        return columns;
    }

    /// <summary>Splits EF's create script into individual statements. The script is generated, so
    /// it holds no string literals containing semicolons to worry about.</summary>
    private static IEnumerable<string> SplitStatements(string script) =>
        script.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
              .Where(s => s.Length > 0);
}
