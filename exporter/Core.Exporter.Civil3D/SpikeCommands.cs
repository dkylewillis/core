using System.Reflection;
using System.Runtime.Loader;
using Autodesk.AutoCAD.ApplicationServices.Core;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.ApplicationServices;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

[assembly: ExtensionApplication(typeof(Core.Exporter.Civil3D.ExporterExtensionApplication))]
[assembly: CommandClass(typeof(Core.Exporter.Civil3D.SpikeCommands))]

namespace Core.Exporter.Civil3D;

/// <summary>
/// NETLOAD entry point. Stays off the Civil API during load so a missing Civil
/// assembly cannot silent-fail Initialize / command registration.
/// Confirms load via Application.Idle (works in both NETLOAD document context
/// and ApplicationPlugins application context).
/// </summary>
public sealed class ExporterExtensionApplication : IExtensionApplication
{
    private static int _idleArmed;
    private static int _resolveHooked;

    public void Initialize()
    {
        try
        {
            WriteLoadLog("Initialize entered");
            HookAssemblyResolve();

            // Defer host UI work to Idle so message + registration side effects
            // run in a stable application context regardless of NETLOAD vs autoload.
            if (Interlocked.Exchange(ref _idleArmed, 1) == 0)
            {
                AcadApp.Idle += OnIdleAnnounce;
            }

            WriteLoadLog("Initialize completed (Idle announce armed)");
        }
        catch (System.Exception ex)
        {
            WriteLoadLog($"Initialize failed: {ex}");
            TryWriteException("Initialize", ex);
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
            // Host may already be tearing down.
        }
    }

    private static void OnIdleAnnounce(object? sender, EventArgs e)
    {
        try
        {
            AcadApp.Idle -= OnIdleAnnounce;
            Interlocked.Exchange(ref _idleArmed, 0);

            var asm = typeof(ExporterExtensionApplication).Assembly;
            var location = asm.Location;
            var tfm =
#if NET10_0_OR_GREATER
                "net10.0-windows";
#else
                "net8.0-windows";
#endif
            var msg =
                $"\nCore.Exporter.Civil3D loaded ({tfm}). " +
                $"Try COREXSPIKE / CSPIKE. DLL: {location}\n";

            var ed = TryGetEditor();
            if (ed is not null)
            {
                ed.WriteMessage(msg);
            }
            else
            {
                // No active document yet — still leave a breadcrumb next to the DLL.
                WriteLoadLog("Idle announce: no active document editor; message skipped");
            }

            WriteLoadLog($"Idle announce ok. Location={location}");
        }
        catch (System.Exception ex)
        {
            WriteLoadLog($"Idle announce failed: {ex}");
            TryWriteException("Idle", ex);
        }
    }

    private static void HookAssemblyResolve()
    {
        if (Interlocked.Exchange(ref _resolveHooked, 1) != 0)
        {
            return;
        }

        // Prefer the plugin folder for any satellite deps that sit next to the DLL
        // (deps.json / EnableDynamicLoading). Never override Autodesk host assemblies.
        AssemblyLoadContext.Default.Resolving += static (_, name) =>
        {
            try
            {
                if (IsAutodeskAssembly(name.Name))
                {
                    return null;
                }

                var dir = Path.GetDirectoryName(typeof(ExporterExtensionApplication).Assembly.Location);
                if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(name.Name))
                {
                    return null;
                }

                var candidate = Path.Combine(dir, name.Name + ".dll");
                if (!File.Exists(candidate))
                {
                    return null;
                }

                WriteLoadLog($"AssemblyResolve loading {candidate}");
                return Assembly.LoadFrom(candidate);
            }
            catch (System.Exception ex)
            {
                WriteLoadLog($"AssemblyResolve failed for {name.Name}: {ex.Message}");
                return null;
            }
        };
    }

    private static bool IsAutodeskAssembly(string? simpleName)
    {
        if (string.IsNullOrEmpty(simpleName))
        {
            return false;
        }

        return simpleName.StartsWith("Acdb", StringComparison.OrdinalIgnoreCase)
            || simpleName.StartsWith("accore", StringComparison.OrdinalIgnoreCase)
            || simpleName.StartsWith("AcMgd", StringComparison.OrdinalIgnoreCase)
            || simpleName.StartsWith("AcCui", StringComparison.OrdinalIgnoreCase)
            || simpleName.StartsWith("AcWindows", StringComparison.OrdinalIgnoreCase)
            || simpleName.StartsWith("AdWindows", StringComparison.OrdinalIgnoreCase)
            || simpleName.StartsWith("AdUI", StringComparison.OrdinalIgnoreCase)
            || simpleName.StartsWith("Aec", StringComparison.OrdinalIgnoreCase)
            || simpleName.Equals("acdbmgdbrep", StringComparison.OrdinalIgnoreCase);
    }

    private static Editor? TryGetEditor()
    {
        var doc = AcadApp.DocumentManager?.MdiActiveDocument;
        return doc?.Editor;
    }

    private static void TryWriteException(string context, System.Exception ex)
    {
        try
        {
            var ed = TryGetEditor();
            ed?.WriteMessage(
                $"\nCore.Exporter.Civil3D {context} failed: {ex.GetType().Name}: {ex.Message}\n");
        }
        catch
        {
            // Last resort: avoid throwing out of Initialize and crashing the host.
        }
    }

    internal static void WriteLoadLog(string line)
    {
        try
        {
            var dir = Path.GetDirectoryName(typeof(ExporterExtensionApplication).Assembly.Location);
            if (string.IsNullOrEmpty(dir))
            {
                return;
            }

            var path = Path.Combine(dir, "Core.Exporter.Civil3D.load.log");
            File.AppendAllText(path, $"{DateTime.UtcNow:o} {line}{Environment.NewLine}");
        }
        catch
        {
            // Logging must never break load.
        }
    }
}

/// <summary>
/// Minimal Civil 3D commands for the exporter spike (docs/EXPORTER_SPIKE.md).
/// Load the TFM-matched DLL with NETLOAD inside Civil 3D, then run COREXSPIKE.
/// AutoCAD identity lines run before any Civil API call so a Civil failure is visible.
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
            ExporterExtensionApplication.WriteLoadLog("COREXSPIKE invoked");

            var doc = AcadApp.DocumentManager.MdiActiveDocument
                ?? throw new InvalidOperationException("No active document.");
            ed = doc.Editor;
            var db = doc.Database;

            // Phase 1: AutoCAD-only — proves command registration without Civil.
            ed.WriteMessage("\nCORE exporter spike probe");
            ed.WriteMessage($"\n  DLL: {typeof(SpikeCommands).Assembly.Location}");
            ed.WriteMessage($"\n  Drawing: {db.Filename}");
            ed.WriteMessage($"\n  FingerprintGuid: {db.FingerprintGuid}");
            ed.WriteMessage($"\n  VersionGuid: {db.VersionGuid}");

            // Phase 2: Civil — isolated so a Civil TypeLoadException still leaves phase 1 output.
            CivilSpike.WriteCivilSummary(ed, db);

            ed.WriteMessage("\n");
            ExporterExtensionApplication.WriteLoadLog("COREXSPIKE completed");
        }
        catch (System.Exception ex)
        {
            ExporterExtensionApplication.WriteLoadLog($"COREXSPIKE failed: {ex}");
            ed ??= AcadApp.DocumentManager?.MdiActiveDocument?.Editor;
            ed?.WriteMessage($"\nCOREXSPIKE failed: {ex.GetType().Name}: {ex.Message}\n");
            throw;
        }
    }
}

/// <summary>
/// Civil API usage lives here so Initialize / AutoCAD phase never JIT Civil types.
/// The assembly still references AeccDbMgd; Civil 3D must supply it at process start.
/// </summary>
internal static class CivilSpike
{
    public static void WriteCivilSummary(Editor ed, Autodesk.AutoCAD.DatabaseServices.Database db)
    {
        try
        {
            var civilDoc = CivilDocument.GetCivilDocument(db);
            ed.WriteMessage($"\n  CivilDocument: {(civilDoc is null ? "null" : "ok")}");
            if (civilDoc is not null)
            {
                ed.WriteMessage($"\n  PipeNetworks: {civilDoc.GetPipeNetworkIds().Count}");
                ed.WriteMessage($"\n  Alignments: {civilDoc.GetAlignmentIds().Count}");
                ed.WriteMessage($"\n  Surfaces: {civilDoc.GetSurfaceIds().Count}");
            }
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage(
                $"\n  Civil probe failed: {ex.GetType().Name}: {ex.Message}" +
                "\n  (AutoCAD phase succeeded — Civil assembly/API issue, not NETLOAD registration.)");
            ExporterExtensionApplication.WriteLoadLog($"CivilSpike failed: {ex}");
        }
    }
}
