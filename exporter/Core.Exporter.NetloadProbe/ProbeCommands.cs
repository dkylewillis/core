using Autodesk.AutoCAD.ApplicationServices.Core;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

[assembly: ExtensionApplication(typeof(Core.Exporter.NetloadProbe.ProbeExtensionApplication))]
[assembly: CommandClass(typeof(Core.Exporter.NetloadProbe.ProbeCommands))]

namespace Core.Exporter.NetloadProbe;

/// <summary>
/// AutoCAD-only NETLOAD diagnostic. No Civil 3D API references.
/// Success proves host/.NET plugin load settings; then try Core.Exporter.Civil3D.
/// </summary>
public sealed class ProbeExtensionApplication : IExtensionApplication
{
    private static int _idleArmed;

    public void Initialize()
    {
        try
        {
            WriteLoadLog("Initialize entered");
            if (Interlocked.Exchange(ref _idleArmed, 1) == 0)
            {
                AcadApp.Idle += OnIdleAnnounce;
            }
        }
        catch (System.Exception ex)
        {
            WriteLoadLog($"Initialize failed: {ex}");
        }
    }

    public void Terminate()
    {
        try
        {
            if (Interlocked.Exchange(ref _idleArmed, 0) == 1)
            {
                AcadApp.Idle -= OnIdleAnnounce;
            }
        }
        catch
        {
        }
    }

    private static void OnIdleAnnounce(object? sender, EventArgs e)
    {
        try
        {
            AcadApp.Idle -= OnIdleAnnounce;
            Interlocked.Exchange(ref _idleArmed, 0);

            var location = typeof(ProbeExtensionApplication).Assembly.Location;
            var msg =
                $"\nCore.Exporter.NetloadProbe loaded (AutoCAD-only). " +
                $"Try COREXPROBE. DLL: {location}\n";
            AcadApp.DocumentManager?.MdiActiveDocument?.Editor?.WriteMessage(msg);
            WriteLoadLog($"Idle announce ok. Location={location}");
        }
        catch (System.Exception ex)
        {
            WriteLoadLog($"Idle announce failed: {ex}");
        }
    }

    internal static void WriteLoadLog(string line)
    {
        try
        {
            var dir = Path.GetDirectoryName(typeof(ProbeExtensionApplication).Assembly.Location);
            if (string.IsNullOrEmpty(dir))
            {
                return;
            }

            File.AppendAllText(
                Path.Combine(dir, "Core.Exporter.NetloadProbe.load.log"),
                $"{DateTime.UtcNow:o} {line}{Environment.NewLine}");
        }
        catch
        {
        }
    }
}

public sealed class ProbeCommands
{
    [CommandMethod("COREXPROBE")]
    public void RunProbe()
    {
        Editor? ed = null;
        try
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument
                ?? throw new InvalidOperationException("No active document.");
            ed = doc.Editor;
            ed.WriteMessage("\nCOREXPROBE ok (AutoCAD-only, no Civil refs).");
            ed.WriteMessage($"\n  DLL: {typeof(ProbeCommands).Assembly.Location}");
            ed.WriteMessage($"\n  Drawing: {doc.Database.Filename}\n");
        }
        catch (System.Exception ex)
        {
            ed ??= AcadApp.DocumentManager?.MdiActiveDocument?.Editor;
            ed?.WriteMessage($"\nCOREXPROBE failed: {ex.GetType().Name}: {ex.Message}\n");
            throw;
        }
    }
}
