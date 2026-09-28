# Civil 3D exporter

Plugin project for the [exporter spike](../docs/EXPORTER_SPIKE.md) and, after ADR-011 is accepted, the COREX exporter ([ADR-012](../docs/adr/ADR-012-implementation-stack.md)).

## Targets and packages

Per [ADR-011](../docs/adr/ADR-011-exporter-runtime.md):

| TFM | Civil 3D | `AutoCAD.NET` | `Civil3D.NET` |
| --- | --- | --- | --- |
| `net8.0-windows` | 2026 (also 2025) | `25.1.0` | `13.8.1516` |
| `net10.0-windows` | 2027 | `26.0.0` | `13.9.628` |

Both packages use `ExcludeAssets="runtime"` so Autodesk assemblies stay with the host process. `EnableWindowsTargeting` in the repo `Directory.Build.props` lets the project restore and compile on Linux; it only runs on Windows with Civil 3D installed.

## Build

```bash
dotnet restore exporter/Core.Exporter.Civil3D/Core.Exporter.Civil3D.csproj
dotnet build exporter/Core.Exporter.Civil3D/Core.Exporter.Civil3D.csproj -c Release
```

Outputs:

- `exporter/Core.Exporter.Civil3D/bin/Release/net8.0-windows/Core.Exporter.Civil3D.dll` — Civil 3D 2025/2026
- `exporter/Core.Exporter.Civil3D/bin/Release/net10.0-windows/Core.Exporter.Civil3D.dll` — Civil 3D 2027

## Load in Civil 3D (spike)

1. Install Civil 3D 2026 (and 2027 if available) on Windows.
2. Build as above (or from Visual Studio).
3. Start Civil 3D and open a representative project drawing.
4. `NETLOAD` the matching DLL from the list above.
5. Run `COREXSPIKE` — it prints fingerprint/version GUIDs and basic Civil object counts for the active document.

Use `accoreconsole.exe /product C3D` for batch probes once interactive reads are confirmed.
