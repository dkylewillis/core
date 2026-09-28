using Microsoft.Data.Sqlite;

namespace Core.Store;

public sealed class CoreStore : IDisposable
{
    private readonly SqliteConnection _connection;
    public bool IsReadOnly { get; }

    private CoreStore(SqliteConnection connection, bool isReadOnly)
    {
        _connection = connection;
        IsReadOnly = isReadOnly;
    }

    public SqliteConnection Connection => _connection;

    public static CoreStore Create(string path, string snapshotId, string projectId, string projectName)
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

        MigrationRunner.ApplySql(connection, MigrationRunner.ReadEmbeddedSql("Core.Store.Migrations.0001_core.sql"));
        MigrationRunner.SetMeta(connection, "schema_version", MigrationRunner.CurrentSchemaVersion.ToString());
        MigrationRunner.SetMeta(connection, "snapshot_id", snapshotId);

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO project(id, name) VALUES ($id, $name);";
            cmd.Parameters.AddWithValue("$id", projectId);
            cmd.Parameters.AddWithValue("$name", projectName);
            cmd.ExecuteNonQuery();
        }

        return new CoreStore(connection, isReadOnly: false);
    }

    public static CoreStore OpenReadOnly(string path)
    {
        var cs = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString();
        var connection = new SqliteConnection(cs);
        connection.Open();
        using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA foreign_keys = ON;";
            pragma.ExecuteNonQuery();
        }
        MigrationRunner.EnsureSupportedVersion(connection, ".core");
        return new CoreStore(connection, isReadOnly: true);
    }

    public static CoreStore OpenReadWriteForImporter(string path)
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
        MigrationRunner.EnsureSupportedVersion(connection, ".core");
        return new CoreStore(connection, isReadOnly: false);
    }

    public IReadOnlyList<string> ListUserTables()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type IN ('table','view') AND name NOT LIKE 'sqlite_%' AND name NOT LIKE 'geometry_rtree_%' ORDER BY name;";
        var list = new List<string>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            list.Add(reader.GetString(0));
        return list;
    }

    public void Dispose() => _connection.Dispose();
}
