using System.IO;
using System.IO.Compression;
using System.Text.Json;
using Core.Corex.Serialization;

namespace Core.Corex.IO
{
    public static class CorexWriter
    {
        public static void WriteDirectory(CorexPackage package, string directory)
        {
            var options = JsonOptions.Create();
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "manifest.json"), JsonSerializer.Serialize(package.Manifest, options));

            var drawingsDir = Path.Combine(directory, "drawings");
            Directory.CreateDirectory(drawingsDir);
            foreach (var entry in package.Manifest.Drawings)
            {
                if (!package.Drawings.TryGetValue(entry.Id, out var drawing))
                    throw new InvalidDataException("Package is missing drawing '" + entry.Id + "'.");
                var path = Path.Combine(directory, entry.Path.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, JsonSerializer.Serialize(drawing, options));
            }

            if (package.Manifest.Sheets != null)
            {
                if (package.Sheets == null)
                    throw new InvalidDataException("Manifest references sheets but package.Sheets is null.");
                var sheetsPath = Path.Combine(directory, package.Manifest.Sheets.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(sheetsPath)!);
                File.WriteAllText(sheetsPath, JsonSerializer.Serialize(package.Sheets, options));
            }

            if (package.Manifest.Shortcuts != null)
            {
                if (package.Shortcuts == null)
                    throw new InvalidDataException("Manifest references shortcuts but package.Shortcuts is null.");
                var shortcutsPath = Path.Combine(directory, package.Manifest.Shortcuts.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(shortcutsPath)!);
                File.WriteAllText(shortcutsPath, JsonSerializer.Serialize(package.Shortcuts, options));
            }

            foreach (var geom in package.Manifest.Geometry)
            {
                if (!package.GeometryBlobs.TryGetValue(geom.Path, out var bytes))
                    continue;
                var geomPath = Path.Combine(directory, geom.Path.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(geomPath)!);
                File.WriteAllBytes(geomPath, bytes);
            }
        }

        public static void WriteZip(CorexPackage package, string zipPath)
        {
            var temp = Path.Combine(Path.GetTempPath(), "corex-write-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            try
            {
                WriteDirectory(package, temp);
                if (File.Exists(zipPath))
                    File.Delete(zipPath);
                ZipFile.CreateFromDirectory(temp, zipPath, CompressionLevel.Optimal, includeBaseDirectory: false);
            }
            finally
            {
                try { Directory.Delete(temp, true); } catch { /* best effort */ }
            }
        }
    }
}
