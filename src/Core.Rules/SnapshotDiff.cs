using System.Text.Json.Nodes;
using Core.Store;

namespace Core.Rules;

public static class SnapshotDiff
{
    public static JsonObject Compare(string oldPath, string newPath)
    {
        using var oldStore = CoreStore.OpenReadOnly(oldPath);
        using var newStore = CoreStore.OpenReadOnly(newPath);
        var oldKeys = LoadDesignKeys(oldStore);
        var newKeys = LoadDesignKeys(newStore);
        var added = newKeys.Keys.Except(oldKeys.Keys).OrderBy(x => x).ToArray();
        var removed = oldKeys.Keys.Except(newKeys.Keys).OrderBy(x => x).ToArray();
        var changed = oldKeys.Keys.Intersect(newKeys.Keys).Where(k => oldKeys[k] != newKeys[k]).OrderBy(x => x).ToArray();
        return new JsonObject
        {
            ["added"] = new JsonArray(added.Select(a => JsonValue.Create(a)).ToArray()),
            ["removed"] = new JsonArray(removed.Select(a => JsonValue.Create(a)).ToArray()),
            ["changed"] = new JsonArray(changed.Select(a => JsonValue.Create(a)).ToArray())
        };
    }

    private static Dictionary<string, string> LoadDesignKeys(CoreStore store)
    {
        using var cmd = store.Connection.CreateCommand();
        cmd.CommandText = "SELECT type || ':' || COALESCE(name,''), properties_json FROM design_object;";
        var dict = new Dictionary<string, string>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            dict[r.GetString(0)] = r.GetString(1);
        return dict;
    }
}
