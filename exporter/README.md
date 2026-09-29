# Civil 3D exporter

Plugin project for the [exporter spike](../docs/EXPORTER_SPIKE.md) and, after ADR-011 is accepted, the COREX exporter ([ADR-012](../docs/adr/ADR-012-implementation-stack.md)).

## Targets and packages

Per [ADR-011](../docs/adr/ADR-011-exporter-runtime.md):

| TFM | Civil 3D | `AutoCAD.NET` | `Civil3D.NET` |
| --- | --- | --- | --- |
| `net8.0-windows` | 2026 (also 2025) | `25.1.0` | `13.8.1516` |
| `net10.0-windows` | 2027 | `26.0.0` | `13.9.628` |

Both packages use `ExcludeAssets="runtime"` so Autodesk assemblies stay with the host process. The project references `Microsoft.WindowsDesktop.App` (Autodesk guidance for .NET 8+ plugins) and sets `PlatformTarget=x64`. `EnableWindowsTargeting` in the repo `Directory.Build.props` lets the project restore and compile on Linux; it only runs on Windows with Civil 3D installed.

## Build

```bash
dotnet restore exporter/Core.Exporter.Civil3D/Core.Exporter.Civil3D.csproj
dotnet build exporter/Core.Exporter.Civil3D/Core.Exporter.Civil3D.csproj -c Release
```

Outputs:

- `exporter/Core.Exporter.Civil3D/bin/Release/net8.0-windows/Core.Exporter.Civil3D.dll` — Civil 3D 2025/2026
- `exporter/Core.Exporter.Civil3D/bin/Release/net10.0-windows/Core.Exporter.Civil3D.dll` — Civil 3D 2027

## Load in Civil 3D (spike)

1. Install **Civil 3D** 2026 (and 2027 if available) on Windows — not plain AutoCAD. The plugin uses Civil APIs and must load inside the Civil 3D product.
2. Build as above (or from Visual Studio).
3. Start Civil 3D and open a representative project drawing.
4. `NETLOAD` the **TFM-matched** DLL from the list above (`net8.0-windows` for 2025/2026, `net10.0-windows` for 2027).
5. On a successful load you should see: `Core.Exporter.Civil3D loaded. Try COREXSPIKE (or CSPIKE).`
6. Run **`COREXSPIKE`** (alias: **`CSPIKE`**) — it prints fingerprint/version GUIDs and basic Civil object counts for the active document.

Use `accoreconsole.exe /product C3D` for batch probes once interactive reads are confirmed.

## Troubleshooting NETLOAD

If `NETLOAD` appears to do nothing, or `COREXSPIKE` / `CSPIKE` are unknown commands:

1. **Product and DLL match**
   - Load inside **Civil 3D**, not AutoCAD alone.
   - Use the DLL whose TFM matches your Civil 3D year (table above). Loading the wrong TFM often fails silently.

2. **SECURELOAD / trusted paths**
   - Check `SECURELOAD` (0 = unrestricted; 1 = warn; 2 = block unsigned / untrusted).
   - Add the folder that contains the DLL to `TRUSTEDPATHS`, or temporarily set `SECURELOAD` to `0` for local spike testing.
   - Prefer copying the DLL to a trusted folder rather than loading from a Downloads path.

3. **Windows Zone.Identifier (blocked download)**
   - If the DLL came from the internet or a zip, Windows may mark it blocked.
   - In File Explorer: Properties → check **Unblock** → Apply.
   - Or PowerShell: `Unblock-File -Path '...\Core.Exporter.Civil3D.dll'`

4. **See the real load error**
   - Press **F2** (or open the text window) immediately after `NETLOAD`. Assembly resolve / security failures often print there and are easy to miss on the command line.
   - A healthy load prints the confirmation from `IExtensionApplication.Initialize` (step 5 above). If that message never appears, the assembly did not initialize.

5. **Command name**
   - The registered commands are **`COREXSPIKE`** and **`CSPIKE`** (not `COREX`, `SPIKE`, etc.).
   - After a successful load, typing either should run the probe without asking to redefine a command.
