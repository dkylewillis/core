using System.Text.Json;
using System.Text.Json.Nodes;
using Core.Store;
using Microsoft.Data.Sqlite;

namespace Core.Import;

public static class SnapshotQueries
{
    public static List<JsonObject> LoadDrawings(CoreStore store)
    {
        using var cmd = store.Connection.CreateCommand();
        cmd.CommandText = "SELECT corex_id, linear_units FROM drawing ORDER BY corex_id;";
        var list = new List<JsonObject>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new JsonObject
            {
                ["id"] = r.GetString(0),
                ["linearUnits"] = r.GetString(1)
            });
        }
        return list;
    }

    public static JsonArray LoadXrefPaths(CoreStore store)
    {
        using var cmd = store.Connection.CreateCommand();
        cmd.CommandText = """
            SELECT xp.id, rd.corex_id AS root, ld.corex_id AS leaf, xp.instance_ids_json, xp.is_loaded, xp.not_loaded_reason, xp.world_transform_json
            FROM xref_path xp
            JOIN drawing rd ON rd.id = xp.root_drawing_id
            LEFT JOIN drawing ld ON ld.id = xp.leaf_drawing_id
            ORDER BY xp.id;
            """;
        var paths = new JsonArray();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var instanceIds = JsonSerializer.Deserialize<List<long>>(r.GetString(3))!;
            var instances = new JsonArray();
            foreach (var iid in instanceIds)
            {
                using var c2 = store.Connection.CreateCommand();
                c2.CommandText = """
                    SELECT d.corex_id, xi.handle FROM xref_instance xi
                    JOIN drawing d ON d.id = xi.host_drawing_id
                    WHERE xi.id = $id;
                    """;
                c2.Parameters.AddWithValue("$id", iid);
                using var r2 = c2.ExecuteReader();
                r2.Read();
                instances.Add(new JsonObject { ["drawing"] = r2.GetString(0), ["handle"] = r2.GetString(1) });
            }

            // Reconstruct path drawing ids from root + instance targets
            var pathDrawings = new JsonArray { r.GetString(1) };
            foreach (var inst in instances)
            {
                var drawing = inst!["drawing"]!.GetValue<string>();
                var handle = inst["handle"]!.GetValue<string>();
                using var c3 = store.Connection.CreateCommand();
                c3.CommandText = """
                    SELECT td.corex_id FROM xref_instance xi
                    JOIN drawing hd ON hd.id = xi.host_drawing_id
                    LEFT JOIN drawing td ON td.id = xi.target_drawing_id
                    WHERE hd.corex_id = $d AND xi.handle = $h;
                    """;
                c3.Parameters.AddWithValue("$d", drawing);
                c3.Parameters.AddWithValue("$h", handle);
                var target = c3.ExecuteScalar() as string;
                pathDrawings.Add(target is null ? null : JsonValue.Create(target));
            }

            var obj = new JsonObject
            {
                ["root"] = r.GetString(1),
                ["path"] = pathDrawings,
                ["instances"] = instances,
                ["loaded"] = r.GetInt64(4) == 1,
                ["reason"] = r.IsDBNull(5) ? null : r.GetString(5),
                ["worldTransform"] = r.IsDBNull(6) ? null : JsonNode.Parse(r.GetString(6))
            };
            paths.Add(obj);
        }
        return paths;
    }

    public static JsonArray LoadDataReferences(CoreStore store)
    {
        using var cmd = store.Connection.CreateCommand();
        cmd.CommandText = """
            SELECT rd.corex_id, se.handle, sd.corex_id, sse.handle, dr.status
            FROM data_reference dr
            JOIN source_entity se ON se.id = dr.reference_entity_id
            JOIN drawing rd ON rd.id = se.drawing_id
            LEFT JOIN source_entity sse ON sse.id = dr.source_entity_id
            LEFT JOIN drawing sd ON sd.id = sse.drawing_id
            ORDER BY rd.corex_id, se.handle;
            """;
        var arr = new JsonArray();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            JsonNode? source = r.IsDBNull(2)
                ? null
                : new JsonObject { ["drawing"] = r.GetString(2), ["handle"] = r.GetString(3) };
            arr.Add(new JsonObject
            {
                ["reference"] = new JsonObject { ["drawing"] = r.GetString(0), ["handle"] = r.GetString(1) },
                ["source"] = source,
                ["status"] = r.GetString(4)
            });
        }
        return arr;
    }

    public static JsonArray LoadDesignObjects(CoreStore store)
    {
        using var cmd = store.Connection.CreateCommand();
        cmd.CommandText = "SELECT id, type, name, system, properties_json FROM design_object ORDER BY id;";
        var arr = new JsonArray();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var id = r.GetInt64(0);
            var mappedFrom = new JsonArray();
            using (var c2 = store.Connection.CreateCommand())
            {
                c2.CommandText = """
                    SELECT d.corex_id, se.handle, em.method, em.via
                    FROM entity_mapping em
                    JOIN source_entity se ON se.id = em.source_entity_id
                    JOIN drawing d ON d.id = se.drawing_id
                    WHERE em.design_object_id = $id
                    ORDER BY em.id;
                    """;
                c2.Parameters.AddWithValue("$id", id);
                using var r2 = c2.ExecuteReader();
                while (r2.Read())
                {
                    var m = new JsonObject
                    {
                        ["drawing"] = r2.GetString(0),
                        ["handle"] = r2.GetString(1),
                        ["method"] = r2.GetString(2)
                    };
                    if (r2.GetString(3) == "data-reference")
                        m["via"] = "data-reference";
                    mappedFrom.Add(m);
                }
            }

            var obj = new JsonObject
            {
                ["type"] = r.GetString(1),
                ["name"] = r.IsDBNull(2) ? null : r.GetString(2),
                ["mappedFrom"] = mappedFrom
            };
            if (!r.IsDBNull(3))
                obj["system"] = r.GetString(3);

            using (var c3 = store.Connection.CreateCommand())
            {
                c3.CommandText = "SELECT property, value, method, rule_ref FROM classification WHERE design_object_id = $id;";
                c3.Parameters.AddWithValue("$id", id);
                using var r3 = c3.ExecuteReader();
                if (r3.Read())
                {
                    obj["classification"] = new JsonObject
                    {
                        ["property"] = r3.GetString(0),
                        ["method"] = r3.GetString(2),
                        ["rule"] = r3.IsDBNull(3) ? null : r3.GetString(3)
                    };
                }
            }

            var props = JsonNode.Parse(r.GetString(4))!.AsObject();
            if (props.Count > 0)
                obj["properties"] = props;

            arr.Add(obj);
        }
        return arr;
    }

    public static JsonArray LoadValidation(CoreStore store)
    {
        using var cmd = store.Connection.CreateCommand();
        cmd.CommandText = "SELECT severity, code, subject_json, message FROM validation_message ORDER BY id;";
        var errors = new JsonArray();
        var warnings = new JsonArray();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var item = new JsonObject { ["code"] = r.GetString(1) };
            if (!r.IsDBNull(2))
            {
                var subj = JsonNode.Parse(r.GetString(2))!;
                item["subject"] = subj;
            }
            if (r.GetString(0) == "error") errors.Add(item);
            else
            {
                // include detail for mixed-units from message when present
                if (r.GetString(1) == "mixed-units-in-xref-chain")
                    item["detail"] = r.GetString(3);
                warnings.Add(item);
            }
        }
        return new JsonArray { errors, warnings };
    }
}
