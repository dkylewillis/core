using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Core.Corex.Validation
{
    public sealed class SchemaValidationError
    {
        public SchemaValidationError(string file, string jsonPath, string message)
        {
            File = file;
            JsonPath = jsonPath;
            Message = message;
        }

        public string File { get; }
        public string JsonPath { get; }
        public string Message { get; }

        public override string ToString() => File + " " + JsonPath + ": " + Message;
    }

    /// <summary>
    /// Validates COREX package files against schemas/corex using check-jsonschema
    /// (same engine as ./scripts/validate.sh). Falls back to structural checks only
    /// when the CLI tool is unavailable.
    /// </summary>
    public sealed class CorexSchemaValidator
    {
        private static readonly Regex ErrorLine = new Regex(
            @"^\s*(?<file>.+?)::(?<path>\$[^:]*):\s*(?<message>.*)$",
            RegexOptions.Compiled);

        private readonly string _schemaDirectory;

        public CorexSchemaValidator(string? schemaDirectory = null)
        {
            _schemaDirectory = schemaDirectory ?? FindDefaultSchemaDirectory();
        }

        public IReadOnlyList<SchemaValidationError> ValidatePackage(string packageDirectory)
        {
            var errors = new List<SchemaValidationError>();
            var manifestPath = Path.Combine(packageDirectory, "manifest.json");
            if (!File.Exists(manifestPath))
            {
                errors.Add(new SchemaValidationError("manifest.json", "$", "File not found."));
                return errors;
            }

            ValidateOne(manifestPath, Rel(packageDirectory, manifestPath), Path.Combine(_schemaDirectory, "manifest.schema.json"), errors);

            using (var doc = JsonDocument.Parse(File.ReadAllText(manifestPath)))
            {
                var root = doc.RootElement;
                if (root.TryGetProperty("drawings", out var drawings))
                {
                    foreach (var d in drawings.EnumerateArray())
                    {
                        var rel = d.GetProperty("path").GetString()!;
                        var full = Path.Combine(packageDirectory, rel.Replace('/', Path.DirectorySeparatorChar));
                        ValidateOne(full, rel, Path.Combine(_schemaDirectory, "drawing.schema.json"), errors);
                    }
                }
                if (root.TryGetProperty("sheets", out var sheets) && sheets.ValueKind == JsonValueKind.String)
                {
                    var rel = sheets.GetString()!;
                    var full = Path.Combine(packageDirectory, rel.Replace('/', Path.DirectorySeparatorChar));
                    ValidateOne(full, rel, Path.Combine(_schemaDirectory, "sheets.schema.json"), errors);
                }
                if (root.TryGetProperty("shortcuts", out var shortcuts) && shortcuts.ValueKind == JsonValueKind.String)
                {
                    var rel = shortcuts.GetString()!;
                    var full = Path.Combine(packageDirectory, rel.Replace('/', Path.DirectorySeparatorChar));
                    ValidateOne(full, rel, Path.Combine(_schemaDirectory, "shortcuts.schema.json"), errors);
                }
            }

            return errors;
        }

        public IReadOnlyList<SchemaValidationError> ValidatePackage(CorexPackage package)
        {
            var temp = Path.Combine(Path.GetTempPath(), "corex-validate-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            try
            {
                IO.CorexWriter.WriteDirectory(package, temp);
                return ValidatePackage(temp);
            }
            finally
            {
                try { Directory.Delete(temp, true); } catch { }
            }
        }

        private void ValidateOne(string instancePath, string relativePath, string schemaPath, List<SchemaValidationError> errors)
        {
            if (!File.Exists(instancePath))
            {
                errors.Add(new SchemaValidationError(relativePath, "$", "File not found."));
                return;
            }

            if (!TryRunCheckJsonSchema(schemaPath, instancePath, relativePath, errors))
            {
                // Minimal fallback when check-jsonschema is not installed.
                try
                {
                    using var _ = JsonDocument.Parse(File.ReadAllText(instancePath));
                }
                catch (Exception ex)
                {
                    errors.Add(new SchemaValidationError(relativePath, "$", "Invalid JSON: " + ex.Message));
                }
            }
        }

        private static bool TryRunCheckJsonSchema(string schemaPath, string instancePath, string relativePath, List<SchemaValidationError> errors)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "check-jsonschema",
                    Arguments = "--schemafile \"" + schemaPath + "\" \"" + instancePath + "\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var process = Process.Start(psi);
                if (process == null)
                    return false;
                var stdout = process.StandardOutput.ReadToEnd();
                var stderr = process.StandardError.ReadToEnd();
                process.WaitForExit();
                var output = stdout + "\n" + stderr;
                if (process.ExitCode == 0)
                    return true;

                var matched = false;
                using (var reader = new StringReader(output))
                {
                    string? line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        var m = ErrorLine.Match(line);
                        if (!m.Success)
                            continue;
                        matched = true;
                        errors.Add(new SchemaValidationError(relativePath, m.Groups["path"].Value, m.Groups["message"].Value.Trim()));
                    }
                }

                if (!matched)
                    errors.Add(new SchemaValidationError(relativePath, "$", output.Trim()));
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string Rel(string root, string full)
        {
            var r = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var f = Path.GetFullPath(full);
            return f.StartsWith(r, StringComparison.OrdinalIgnoreCase)
                ? f.Substring(r.Length).Replace(Path.DirectorySeparatorChar, '/')
                : Path.GetFileName(full);
        }

        private static string FindDefaultSchemaDirectory()
        {
            var candidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "schemas", "corex"),
                Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "schemas", "corex")),
                Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "schemas", "corex"))
            };
            foreach (var c in candidates)
            {
                if (Directory.Exists(c) && File.Exists(Path.Combine(c, "manifest.schema.json")))
                    return c;
            }
            throw new DirectoryNotFoundException("Could not locate schemas/corex directory.");
        }
    }
}
