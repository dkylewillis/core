using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Core.Corex
{
    public sealed class EntityRef
    {
        public string Drawing { get; set; } = "";
        public string Handle { get; set; } = "";
    }

    public sealed class UnitsInfo
    {
        public int Insunits { get; set; }
        public string Linear { get; set; } = "";
    }

    public sealed class ResolutionInfo
    {
        public string SavedPath { get; set; } = "";
        public string? ResolvedPath { get; set; }
        public string ResolvedBy { get; set; } = "";
        public string Status { get; set; } = "";
    }

    public sealed class OcsInfo
    {
        public double[] Normal { get; set; } = new double[3];
        public double Elevation { get; set; }
    }

    public sealed class GeometryPayload
    {
        public string Type { get; set; } = "";
        public double[]? Position { get; set; }
        public double[]? Start { get; set; }
        public double[]? End { get; set; }
        public double[]? Center { get; set; }
        public double? Radius { get; set; }
        public double? StartAngle { get; set; }
        public double? EndAngle { get; set; }
        public List<double[]>? Vertices { get; set; }
        public List<double>? Bulges { get; set; }
        public bool? Closed { get; set; }
        public OcsInfo? Ocs { get; set; }
        public string? Path { get; set; }
        public string? Sha256 { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtensionData { get; set; }
    }
}
