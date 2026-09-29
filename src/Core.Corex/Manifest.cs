using System;
using System.Collections.Generic;

namespace Core.Corex
{
    public sealed class Manifest
    {
        public string Schema { get; set; } = "corex.manifest/1";
        public string CorexVersion { get; set; } = "1.0";
        public ExporterInfo Exporter { get; set; } = new ExporterInfo();
        public HostInfo Host { get; set; } = new HostInfo();
        public DateTimeOffset ExportedAt { get; set; }
        public ProjectInfo Project { get; set; } = new ProjectInfo();
        public StartingPointInfo StartingPoint { get; set; } = new StartingPointInfo();
        public List<ManifestDrawing> Drawings { get; set; } = new List<ManifestDrawing>();
        public string? Shortcuts { get; set; }
        public string? Sheets { get; set; }
        public List<ManifestGeometry> Geometry { get; set; } = new List<ManifestGeometry>();
        public List<ManifestDependency> Dependencies { get; set; } = new List<ManifestDependency>();
    }

    public sealed class ExporterInfo
    {
        public string Name { get; set; } = "";
        public string Version { get; set; } = "";
    }

    public sealed class HostInfo
    {
        public string Product { get; set; } = "";
        public string Version { get; set; } = "";
        public string Runtime { get; set; } = "";
        public string Mode { get; set; } = "";
    }

    public sealed class ProjectInfo
    {
        public string Name { get; set; } = "";
        public string Root { get; set; } = "";
    }

    public sealed class StartingPointInfo
    {
        public string Kind { get; set; } = "";
        public string Path { get; set; } = "";
    }

    public sealed class ManifestDrawing
    {
        public string Id { get; set; } = "";
        public string Path { get; set; } = "";
        public string SourcePath { get; set; } = "";
        public string SourceSha256 { get; set; } = "";
        public string FingerprintGuid { get; set; } = "";
    }

    public sealed class ManifestGeometry
    {
        public string Path { get; set; } = "";
        public string Sha256 { get; set; } = "";
        public string Kind { get; set; } = "";
    }

    public sealed class ManifestDependency
    {
        public string From { get; set; } = "";
        public string? To { get; set; }
        public string? ToPath { get; set; }
        public string Kind { get; set; } = "";
        public string Status { get; set; } = "";
    }
}
