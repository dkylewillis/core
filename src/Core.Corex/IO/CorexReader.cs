using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using Core.Corex.Serialization;

namespace Core.Corex.IO
{
    public static class CorexReader
    {
        public static CorexPackage Read(string path)
        {
            if (Directory.Exists(path))
                return ReadDirectory(path);
            if (File.Exists(path))
                return ReadZip(path);
            throw new FileNotFoundException("COREX package not found.", path);
        }

        public static CorexPackage ReadDirectory(string directory)
        {
            var options = JsonOptions.Create();
            var manifestPath = Path.Combine(directory, "manifest.json");
            if (!File.Exists(manifestPath))
                throw new InvalidDataException("Package is missing manifest.json.");

            var package = new CorexPackage
            {
                Manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(manifestPath), options)
                    ?? throw new InvalidDataException("Failed to deserialize manifest.json.")
            };

            foreach (var drawingEntry in package.Manifest.Drawings)
            {
                var drawingPath = Path.Combine(directory, drawingEntry.Path.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(drawingPath))
                    throw new InvalidDataException("Missing drawing file: " + drawingEntry.Path);
                var drawing = JsonSerializer.Deserialize<DrawingDocument>(File.ReadAllText(drawingPath), options)
                    ?? throw new InvalidDataException("Failed to deserialize " + drawingEntry.Path);
                package.Drawings[drawingEntry.Id] = drawing;
            }

            if (package.Manifest.Sheets != null)
            {
                var sheetsPath = Path.Combine(directory, package.Manifest.Sheets.Replace('/', Path.DirectorySeparatorChar));
                package.Sheets = JsonSerializer.Deserialize<SheetsDocument>(File.ReadAllText(sheetsPath), options);
            }

            if (package.Manifest.Shortcuts != null)
            {
                var shortcutsPath = Path.Combine(directory, package.Manifest.Shortcuts.Replace('/', Path.DirectorySeparatorChar));
                package.Shortcuts = JsonSerializer.Deserialize<ShortcutsDocument>(File.ReadAllText(shortcutsPath), options);
            }

            foreach (var geom in package.Manifest.Geometry)
            {
                var geomPath = Path.Combine(directory, geom.Path.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(geomPath))
                    package.GeometryBlobs[geom.Path] = File.ReadAllBytes(geomPath);
            }

            return package;
        }

        public static CorexPackage ReadZip(string zipPath)
        {
            var temp = Path.Combine(Path.GetTempPath(), "corex-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            try
            {
                ZipFile.ExtractToDirectory(zipPath, temp);
                return ReadDirectory(temp);
            }
            finally
            {
                try { Directory.Delete(temp, true); } catch { /* best effort */ }
            }
        }
    }
}
