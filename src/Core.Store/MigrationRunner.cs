using System.Reflection;
using Microsoft.Data.Sqlite;

namespace Core.Store;

public static class MigrationRunner
{
    public const int CurrentSchemaVersion = 1;

    public static string ReadEmbeddedSql(string resourceName)
    {
        var asm = typeof(MigrationRunner).Assembly;
        using var stream = asm.GetManifestResourceStream(resourceName)
            ?? throw new StoreException("Missing embedded migration resource: " + resourceName);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static void ApplySql(SqliteConnection connection, string sql)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public static void SetMeta(SqliteConnection connection, string key, string value)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "INSERT INTO meta(key, value) VALUES ($k, $v) ON CONFLICT(key) DO UPDATE SET value = excluded.value;";
        cmd.Parameters.AddWithValue("$k", key);
        cmd.Parameters.AddWithValue("$v", value);
        cmd.ExecuteNonQuery();
    }

    public static string? GetMeta(SqliteConnection connection, string key)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT value FROM meta WHERE key = $k;";
        cmd.Parameters.AddWithValue("$k", key);
        return cmd.ExecuteScalar() as string;
    }

    public static void EnsureSupportedVersion(SqliteConnection connection, string storeKind)
    {
        var versionText = GetMeta(connection, "schema_version");
        if (versionText is null)
            throw new StoreException($"The {storeKind} store has no schema_version and cannot be opened.");
        if (!int.TryParse(versionText, out var version))
            throw new StoreException($"The {storeKind} store has an invalid schema_version '{versionText}'.");
        if (version != CurrentSchemaVersion)
            throw new StoreException(
                $"Unsupported {storeKind} schema version {version}. This build supports version {CurrentSchemaVersion} only.");
    }
}
