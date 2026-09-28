using Microsoft.Data.Sqlite;

namespace Core.Store;

public sealed class ReviewStore : IDisposable
{
    private readonly SqliteConnection _connection;
    public SqliteConnection Connection => _connection;

    private ReviewStore(SqliteConnection connection) => _connection = connection;

    public static ReviewStore Create(string path, string projectId)
    {
        if (File.Exists(path))
            File.Delete(path);

        var cs = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ToString();
        var connection = new SqliteConnection(cs);
        connection.Open();
        using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA foreign_keys = ON;";
            pragma.ExecuteNonQuery();
        }

        MigrationRunner.ApplySql(connection, MigrationRunner.ReadEmbeddedSql("Core.Store.Migrations.0001_corereview.sql"));
        MigrationRunner.SetMeta(connection, "schema_version", MigrationRunner.CurrentSchemaVersion.ToString());
        MigrationRunner.SetMeta(connection, "project_id", projectId);
        return new ReviewStore(connection);
    }

    public static ReviewStore Open(string path)
    {
        var cs = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false
        }.ToString();
        var connection = new SqliteConnection(cs);
        connection.Open();
        using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA foreign_keys = ON;";
            pragma.ExecuteNonQuery();
        }

        var versionText = MigrationRunner.GetMeta(connection, "schema_version");
        if (versionText is null)
            throw new StoreException("The .corereview store has no schema_version and cannot be opened.");
        if (!int.TryParse(versionText, out var version))
            throw new StoreException($"The .corereview store has an invalid schema_version '{versionText}'.");
        if (version < MigrationRunner.CurrentSchemaVersion)
        {
            // Future: apply numbered migrations in place. Version 1 has none beyond 0001.
            throw new StoreException(
                $"Unsupported .corereview schema version {version}. This build supports version {MigrationRunner.CurrentSchemaVersion} only.");
        }
        if (version > MigrationRunner.CurrentSchemaVersion)
            throw new StoreException(
                $"Unsupported .corereview schema version {version}. This build supports version {MigrationRunner.CurrentSchemaVersion} only.");

        return new ReviewStore(connection);
    }

    public IReadOnlyList<string> ListUserTables()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type IN ('table','view') AND name NOT LIKE 'sqlite_%' ORDER BY name;";
        var list = new List<string>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            list.Add(reader.GetString(0));
        return list;
    }

    public void Dispose() => _connection.Dispose();
}
