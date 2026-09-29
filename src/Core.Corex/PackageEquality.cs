using System.Text.Json;
using System.Text.Json.Nodes;
using Core.Corex.IO;
using Core.Corex.Serialization;

namespace Core.Corex
{
    public static class PackageEquality
    {
        public static bool DeepEquals(CorexPackage a, CorexPackage b)
        {
            var options = JsonOptions.Create();
            var ja = JsonSerializer.SerializeToNode(a.Manifest, options);
            var jb = JsonSerializer.SerializeToNode(b.Manifest, options);
            if (!JsonNode.DeepEquals(ja, jb))
                return false;

            if (a.Drawings.Count != b.Drawings.Count)
                return false;
            foreach (var kv in a.Drawings)
            {
                if (!b.Drawings.TryGetValue(kv.Key, out var other))
                    return false;
                if (!JsonNode.DeepEquals(
                        JsonSerializer.SerializeToNode(kv.Value, options),
                        JsonSerializer.SerializeToNode(other, options)))
                    return false;
            }

            if (!JsonNode.DeepEquals(
                    JsonSerializer.SerializeToNode(a.Sheets, options),
                    JsonSerializer.SerializeToNode(b.Sheets, options)))
                return false;

            if (!JsonNode.DeepEquals(
                    JsonSerializer.SerializeToNode(a.Shortcuts, options),
                    JsonSerializer.SerializeToNode(b.Shortcuts, options)))
                return false;

            if (a.GeometryBlobs.Count != b.GeometryBlobs.Count)
                return false;
            foreach (var kv in a.GeometryBlobs)
            {
                if (!b.GeometryBlobs.TryGetValue(kv.Key, out var bytes))
                    return false;
                if (kv.Value.Length != bytes.Length)
                    return false;
                for (var i = 0; i < bytes.Length; i++)
                    if (kv.Value[i] != bytes[i])
                        return false;
            }

            return true;
        }
    }
}
