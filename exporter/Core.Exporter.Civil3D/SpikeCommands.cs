using Autodesk.AutoCAD.ApplicationServices.Core;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.ApplicationServices;

[assembly: CommandClass(typeof(Core.Exporter.Civil3D.SpikeCommands))]

namespace Core.Exporter.Civil3D;

/// <summary>
/// Minimal Civil 3D commands for the exporter spike (docs/EXPORTER_SPIKE.md).
/// Load the TFM-matched DLL with NETLOAD inside Civil 3D, then run COREXSPIKE.
/// </summary>
public sealed class SpikeCommands
{
    [CommandMethod("COREXSPIKE")]
    public void RunSpikeProbe()
    {
        var doc = Application.DocumentManager.MdiActiveDocument
            ?? throw new InvalidOperationException("No active document.");
        var ed = doc.Editor;
        var db = doc.Database;

        ed.WriteMessage("\nCORE exporter spike probe");
        ed.WriteMessage($"\n  Drawing: {db.Filename}");
        ed.WriteMessage($"\n  FingerprintGuid: {db.FingerprintGuid}");
        ed.WriteMessage($"\n  VersionGuid: {db.VersionGuid}");

        var civilDoc = CivilDocument.GetCivilDocument(db);
        ed.WriteMessage($"\n  CivilDocument: {(civilDoc is null ? "null" : "ok")}");
        if (civilDoc is not null)
        {
            ed.WriteMessage($"\n  PipeNetworks: {civilDoc.GetPipeNetworkIds().Count}");
            ed.WriteMessage($"\n  Alignments: {civilDoc.GetAlignmentIds().Count}");
            ed.WriteMessage($"\n  Surfaces: {civilDoc.GetSurfaceIds().Count}");
        }

        ed.WriteMessage("\n");
    }
}
