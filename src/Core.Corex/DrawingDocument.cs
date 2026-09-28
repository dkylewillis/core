using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Core.Corex
{
    public sealed class DrawingDocument
    {
        public string Schema { get; set; } = "corex.drawing/1";
        public string Id { get; set; } = "";
        public DrawingFileInfo File { get; set; } = new DrawingFileInfo();
        public string FingerprintGuid { get; set; } = "";
        public string VersionGuid { get; set; } = "";
        public SavedByInfo SavedBy { get; set; } = new SavedByInfo();
        public UnitsInfo Units { get; set; } = new UnitsInfo();
        public DrawingSettings Settings { get; set; } = new DrawingSettings();
        public CivilSettings? Civil { get; set; }
        public List<LayerInfo> Layers { get; set; } = new List<LayerInfo>();
        public List<BlockDefinitionInfo> BlockDefinitions { get; set; } = new List<BlockDefinitionInfo>();
        public List<Entity> Entities { get; set; } = new List<Entity>();
        public List<XrefInfo> Xrefs { get; set; } = new List<XrefInfo>();
        public List<StyleInfo> Styles { get; set; } = new List<StyleInfo>();
        public List<LayoutInfo> Layouts { get; set; } = new List<LayoutInfo>();
    }

    public sealed class DrawingFileInfo
    {
        public string SourcePath { get; set; } = "";
        public long SizeBytes { get; set; }
        public string Sha256 { get; set; } = "";
        public System.DateTimeOffset? LastSaved { get; set; }
    }

    public sealed class SavedByInfo
    {
        public string Product { get; set; } = "";
        public string Version { get; set; } = "";
    }

    public sealed class DrawingSettings
    {
        public bool Visretain { get; set; }
    }

    public sealed class CivilSettings
    {
        public string? CoordinateSystemCode { get; set; }
        public double? DrawingScale { get; set; }
        public double? GridToGroundScale { get; set; }
    }

    public sealed class LayerInfo
    {
        public string Name { get; set; } = "";
        public bool On { get; set; }
        public bool Frozen { get; set; }
        public bool Locked { get; set; }
        public bool Plot { get; set; }
        public string? DependentXref { get; set; }
    }

    public sealed class BlockDefinitionInfo
    {
        public string Handle { get; set; } = "";
        public string Name { get; set; } = "";
        public string Kind { get; set; } = "";
        public double[] Origin { get; set; } = new double[3];
    }

    public sealed class StyleInfo
    {
        public string Handle { get; set; } = "";
        public string Name { get; set; } = "";
        public string Kind { get; set; } = "";
        public string ObjectType { get; set; } = "";
        public List<DisplayComponentInfo> Components { get; set; } = new List<DisplayComponentInfo>();
    }

    public sealed class DisplayComponentInfo
    {
        public string Name { get; set; } = "";
        public string View { get; set; } = "";
        public bool Visible { get; set; }
        public string Layer { get; set; } = "";
    }

    public sealed class Entity
    {
        public string Handle { get; set; } = "";
        public string Kind { get; set; } = "";
        [JsonPropertyName("class")]
        public string Class { get; set; } = "";
        public string? Name { get; set; }
        public string? Description { get; set; }
        public string Space { get; set; } = "";
        public string? Owner { get; set; }
        public string Layer { get; set; } = "";
        public bool? Visible { get; set; }
        public string? Style { get; set; }
        public bool? Proxy { get; set; }
        public DataReferencePayload? Reference { get; set; }
        public AttachedDataPayload? AttachedData { get; set; }
        public Dictionary<string, JsonElement>? Properties { get; set; }
        public GeometryPayload? Geometry { get; set; }
        public TextPayload? Text { get; set; }
        public LeaderPayload? Leader { get; set; }
        public BlockReferencePayload? Block { get; set; }
        public NetworkPayload? Network { get; set; }
        public PipePayload? Pipe { get; set; }
        public StructurePayload? Structure { get; set; }
        public PressurePartPayload? PressurePart { get; set; }
        public AlignmentPayload? Alignment { get; set; }
        public ProfilePayload? Profile { get; set; }
        public ProfileViewPayload? ProfileView { get; set; }
        public ViewFramePayload? ViewFrame { get; set; }
        public LabelPayload? Label { get; set; }
    }

    public sealed class DataReferencePayload
    {
        public string Shortcut { get; set; } = "";
        public EntityRef? Source { get; set; }
        public string Status { get; set; } = "";
    }

    public sealed class AttachedDataPayload
    {
        public List<ObjectDataRecord>? ObjectData { get; set; }
        public List<PropertySetRecord>? PropertySets { get; set; }
    }

    public sealed class ObjectDataRecord
    {
        public string Table { get; set; } = "";
        public Dictionary<string, JsonElement> Fields { get; set; } = new Dictionary<string, JsonElement>();
    }

    public sealed class PropertySetRecord
    {
        public string Name { get; set; } = "";
        public Dictionary<string, JsonElement> Properties { get; set; } = new Dictionary<string, JsonElement>();
    }

    public sealed class TextPayload
    {
        public string Contents { get; set; } = "";
        public string Plain { get; set; } = "";
        public double[] Location { get; set; } = new double[3];
        public double Height { get; set; }
        public double Rotation { get; set; }
    }

    public sealed class LeaderPayload
    {
        public List<double[]> Vertices { get; set; } = new List<double[]>();
    }

    public sealed class AttributePayload
    {
        public string Tag { get; set; } = "";
        public string Value { get; set; } = "";
        public string? Handle { get; set; }
    }

    public sealed class BlockReferencePayload
    {
        public string Definition { get; set; } = "";
        public double[] Position { get; set; } = new double[3];
        public double[] ScaleFactors { get; set; } = new double[3];
        public double Rotation { get; set; }
        public double[] Normal { get; set; } = new double[3];
        public double[] Transform { get; set; } = new double[16];
        public List<AttributePayload>? Attributes { get; set; }
    }

    public sealed class NetworkPayload
    {
        public List<string> Parts { get; set; } = new List<string>();
    }

    public sealed class PipePayload
    {
        public string Network { get; set; } = "";
        public string? StartStructure { get; set; }
        public string? EndStructure { get; set; }
        public double[] StartPoint { get; set; } = new double[3];
        public double[] EndPoint { get; set; } = new double[3];
        public double InnerDiameter { get; set; }
        public double? OuterDiameter { get; set; }
        public string Shape { get; set; } = "";
        public string Material { get; set; } = "";
        public string PartFamily { get; set; } = "";
        public string PartSize { get; set; } = "";
    }

    public sealed class StructurePayload
    {
        public string Network { get; set; } = "";
        public double[] Position { get; set; } = new double[3];
        public double RimElevation { get; set; }
        public double SumpElevation { get; set; }
        public List<string> ConnectedPipes { get; set; } = new List<string>();
        public double? InnerDiameterOrWidth { get; set; }
        public string PartFamily { get; set; } = "";
        public string PartSize { get; set; } = "";
    }

    public sealed class PressurePartPayload
    {
        public string Network { get; set; } = "";
        public double[]? StartPoint { get; set; }
        public double[]? EndPoint { get; set; }
        public double[]? Position { get; set; }
        public List<string?> ConnectedParts { get; set; } = new List<string?>();
        public double? InnerDiameter { get; set; }
        public string? Material { get; set; }
        public string PartSize { get; set; } = "";
    }

    public sealed class AlignmentPayload
    {
        public double StartStation { get; set; }
        public List<AlignmentSegment> Segments { get; set; } = new List<AlignmentSegment>();
    }

    public sealed class AlignmentSegment
    {
        public string Type { get; set; } = "";
        public double[] Start { get; set; } = new double[2];
        public double[] End { get; set; } = new double[2];
        public double StartStation { get; set; }
        public double Length { get; set; }
        public double[]? Center { get; set; }
        public double? Radius { get; set; }
        public bool? Clockwise { get; set; }
        public double? RadiusIn { get; set; }
        public double? RadiusOut { get; set; }
        public string? SpiralDefinition { get; set; }
    }

    public sealed class ProfilePayload
    {
        public string Alignment { get; set; } = "";
        public string ProfileType { get; set; } = "";
        public List<ProfilePvi> Pvis { get; set; } = new List<ProfilePvi>();
    }

    public sealed class ProfilePvi
    {
        public double Station { get; set; }
        public double Elevation { get; set; }
        public double CurveLength { get; set; }
    }

    public sealed class ProfileViewPayload
    {
        public string Alignment { get; set; } = "";
        public List<string> Profiles { get; set; } = new List<string>();
        public double StationStart { get; set; }
        public double StationEnd { get; set; }
        public double ElevationMin { get; set; }
        public double ElevationMax { get; set; }
        public double VerticalExaggeration { get; set; }
        public double[] Origin { get; set; } = new double[2];
    }

    public sealed class ViewFramePayload
    {
        public string Group { get; set; } = "";
        public string? SheetName { get; set; }
        public List<double[]> Boundary { get; set; } = new List<double[]>();
        public string? Alignment { get; set; }
        public double? StationStart { get; set; }
        public double? StationEnd { get; set; }
    }

    public sealed class LabelPayload
    {
        public string Annotates { get; set; } = "";
        public double[] Anchor { get; set; } = new double[3];
        public string? Text { get; set; }
        public bool TextOverridden { get; set; }
    }

    public sealed class XrefInfo
    {
        public string Handle { get; set; } = "";
        public string BlockName { get; set; } = "";
        public string Definition { get; set; } = "";
        public string? Target { get; set; }
        public string Attachment { get; set; } = "";
        public ResolutionInfo Resolution { get; set; } = new ResolutionInfo();
        public string Space { get; set; } = "";
        public string? Owner { get; set; }
        public string Layer { get; set; } = "";
        public bool? Visible { get; set; }
        public double[] Position { get; set; } = new double[3];
        public double[] ScaleFactors { get; set; } = new double[3];
        public double Rotation { get; set; }
        public double[] Normal { get; set; } = new double[3];
        public double UnitScale { get; set; } = 1.0;
        public double[] Transform { get; set; } = new double[16];
        public XrefClip? Clip { get; set; }
    }

    public sealed class XrefClip
    {
        public List<double[]> Boundary { get; set; } = new List<double[]>();
        public bool Inverted { get; set; }
        public bool Enabled { get; set; }
    }

    public sealed class LayoutInfo
    {
        public string Handle { get; set; } = "";
        public string Name { get; set; } = "";
        public int TabOrder { get; set; }
        public string? PaperUnits { get; set; }
        public TitleBlockInfo? TitleBlock { get; set; }
        public List<ViewportInfo> Viewports { get; set; } = new List<ViewportInfo>();
    }

    public sealed class TitleBlockInfo
    {
        public string BlockReference { get; set; } = "";
        public Dictionary<string, string> Attributes { get; set; } = new Dictionary<string, string>();
    }

    public sealed class ViewportInfo
    {
        public string Handle { get; set; } = "";
        public int Number { get; set; }
        public bool IsPaperSpaceView { get; set; }
        public bool On { get; set; } = true;
        public string? Layer { get; set; }
        public double[] CenterPaper { get; set; } = new double[2];
        public double Width { get; set; }
        public double Height { get; set; }
        public List<double[]>? ClipBoundary { get; set; }
        public ViewportView? View { get; set; }
        public string? AnnotationScale { get; set; }
        public List<string>? FrozenLayers { get; set; }
    }

    public sealed class ViewportView
    {
        public double[] Center { get; set; } = new double[2];
        public double[] Target { get; set; } = new double[3];
        public double[] Direction { get; set; } = new double[3];
        public double Height { get; set; }
        public double CustomScale { get; set; }
        public double Twist { get; set; }
    }
}
