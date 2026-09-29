using Autodesk.AutoCAD.ApplicationServices.Core;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.ApplicationServices;

[assembly: ExtensionApplication(typeof(Core.Exporter.Civil3D.ExporterExtensionApplication))]
[assembly: CommandClass(typeof(Core.Exporter.Civil3D.SpikeCommands))]

namespace Core.Exporter.Civil3D;

/// <summary>
/// Prints a clear load confirmation so NETLOAD failures are not silent.
/// </summary>
public sealed class ExporterExtensionApplication : IExtensionApplication
{
    public void Initialize()
    {
        try
        {
            var ed = TryGetEditor();
            ed?.WriteMessage("\nCore.Exporter.Civil3D loaded. Try COREXSPIKE (or CSPIKE).\n");
        }
        catch (System.Exception ex)
        {
            TryWriteException("Initialize", ex);
        }
    }

    public void Terminate()
    {
    }

    private static Editor? TryGetEditor()
    {
        var doc = Application.DocumentManager?.MdiActiveDocument;
        return doc?.Editor;
    }

    private static void TryWriteException(string context, System.Exception ex)
    {
        try
        {
            var ed = TryGetEditor();
            ed?.WriteMessage($"\nCore.Exporter.Civil3D {context} failed: {ex.GetType().Name}: {ex.Message}\n");
        }
        catch
        {
            // Last resort: avoid throwing out of Initialize and crashing the host.
        }
    }
}

/// <summary>
/// Minimal Civil 3D commands for the exporter spike (docs/EXPORTER_SPIKE.md).
/// Load the TFM-matched DLL with NETLOAD inside Civil 3D, then run COREXSPIKE.
/// </summary>
public sealed class SpikeCommands
{
    [CommandMethod("COREXSPIKE")]
    [CommandMethod("CSPIKE")]
    public void RunSpikeProbe()
    {
        Editor? ed = null;
        try
        {
            var doc = Application.DocumentManager.MdiActiveDocument
                ?? throw new InvalidOperationException("No active document.");
            ed = doc.Editor;
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
        catch (System.Exception ex)
        {
            ed ??= Application.DocumentManager?.MdiActiveDocument?.Editor;
            ed?.WriteMessage($"\nCOREXSPIKE failed: {ex.GetType().Name}: {ex.Message}\n");
            throw;
        }
    }
}
