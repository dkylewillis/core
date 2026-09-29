# Civil 3D exporter

Plugin project for the [exporter spike](../docs/EXPORTER_SPIKE.md) and, after ADR-011 is accepted, the COREX exporter ([ADR-012](../docs/adr/ADR-012-implementation-stack.md)).

## Targets and packages

Per [ADR-011](../docs/adr/ADR-011-exporter-runtime.md):

| TFM | Civil 3D | `AutoCAD.NET` | `Civil3D.NET` |
| --- | --- | --- | --- |
| `net8.0-windows` | 2026 (also 2025) | `25.1.0` | `13.8.1516` |
| `net10.0-windows` | 2027 | `26.0.0` | `13.9.628` |

Both packages use `ExcludeAssets="runtime"` so Autodesk assemblies stay with the host process. The project:

- references `Microsoft.WindowsDesktop.App` (Autodesk guidance for .NET 8+ plugins)
- sets `PlatformTarget=x64` (not `RuntimeIdentifier` — that creates a `win-x64` subfolder Autodesk does not probe)
- sets `EnableDynamicLoading=true` so `deps.json` + `runtimeconfig.json` are emitted for NETLOAD
- sets `GenerateTargetFrameworkAttribute=false` (Autodesk template requirement — avoids host attribute conflicts)

`EnableWindowsTargeting` in the repo `Directory.Build.props` lets the project restore and compile on Linux; it only runs on Windows with Civil 3D installed.

## Build

```bash
dotnet restore exporter/Core.Exporter.Civil3D/Core.Exporter.Civil3D.csproj
dotnet build exporter/Core.Exporter.Civil3D/Core.Exporter.Civil3D.csproj -c Release
```

Optional AutoCAD-only diagnostic probe (no Civil refs):

```bash
dotnet build exporter/Core.Exporter.NetloadProbe/Core.Exporter.NetloadProbe.csproj -c Release
```

Outputs (load the **whole folder**, not a lone DLL copied elsewhere without sidecars):

| Civil 3D | Folder |
| --- | --- |
| 2025 / 2026 | `exporter/Core.Exporter.Civil3D/bin/Release/net8.0-windows/` |
| 2027 | `exporter/Core.Exporter.Civil3D/bin/Release/net10.0-windows/` |

Required files next to `Core.Exporter.Civil3D.dll`:

- `Core.Exporter.Civil3D.deps.json`
- `Core.Exporter.Civil3D.runtimeconfig.json`

## Load in Civil 3D (spike)

1. Install **Civil 3D** 2026 (and 2027 if available) on Windows — not plain AutoCAD. The Civil plugin references `AeccDbMgd` and must load inside the Civil 3D product.
2. Build as above (or from Visual Studio).
3. Start Civil 3D and open a drawing (active document required for the confirmation line).
4. Set `SECURELOAD` to `0` for this session (or add the output folder to `TRUSTEDPATHS`).
5. `NETLOAD` the **TFM-matched** `Core.Exporter.Civil3D.dll` from the table above.
6. Press **F2**. On success you must see:
   `Core.Exporter.Civil3D loaded (net8.0-windows). Try COREXSPIKE / CSPIKE. DLL: …`
   (or `net10.0-windows` for 2027).
7. Run **`COREXSPIKE`** (alias: **`CSPIKE`**). You must see fingerprint/version GUIDs, then Civil object counts (or a clear Civil-phase error after the AutoCAD lines).

Also check for `Core.Exporter.Civil3D.load.log` next to the DLL — `Initialize` / Idle / command breadcrumbs are appended even when the editor message is missed.

Use `accoreconsole.exe /product C3D` for batch probes once interactive reads are confirmed.

## Diagnostic split: AutoCAD-only probe

If Civil NETLOAD still fails, isolate with the AutoCAD-only assembly:

1. `NETLOAD` `exporter/Core.Exporter.NetloadProbe/bin/Release/<tfm>/Core.Exporter.NetloadProbe.dll`
2. Expect: `Core.Exporter.NetloadProbe loaded (AutoCAD-only). Try COREXPROBE.`
3. Run **`COREXPROBE`**.

| Result | Meaning |
| --- | --- |
| Probe loads, Civil DLL does not | Civil / `AeccDbMgd` resolution or wrong product (plain AutoCAD). Stay in Civil 3D; confirm TFM year. |
| Neither loads | Host/.NET plugin settings: `SECURELOAD`, Zone.Identifier, wrong TFM, missing `deps.json`/`runtimeconfig.json`, or blocked path. |
| Both load; Civil command fails mid-probe | NETLOAD is fine; Civil API/document issue (see F2 / load.log). |

## Troubleshooting NETLOAD — checklist

Work top to bottom. After each change, restart Civil 3D or at least re-`NETLOAD` and press **F2**.

1. **Product**
   - Process must be **Civil 3D** (`acad.exe` started with `/product C3D`), not AutoCAD alone.
   - Confirm in the title bar / About dialog.

2. **Exact DLL path and TFM**
   - 2025/2026 → `...\bin\Release\net8.0-windows\Core.Exporter.Civil3D.dll`
   - 2027 → `...\bin\Release\net10.0-windows\Core.Exporter.Civil3D.dll`
   - Do **not** load from `win-x64\` subfolders or from a copy that omitted `deps.json` / `runtimeconfig.json`.

3. **SECURELOAD / TRUSTEDPATHS**
   - Type `SECURELOAD` — note the value (`0` unrestricted, `1` warn, `2` block).
   - For spike testing: `SECURELOAD` → `0`, or append the build output folder to `TRUSTEDPATHS`.
   - Prefer a local trusted folder over Downloads / network / zip extracts.

4. **Windows Zone.Identifier (blocked file)**
   - File Explorer → DLL Properties → **Unblock** → Apply.
   - Or: `Unblock-File -Path '...\Core.Exporter.Civil3D.dll'` (and the `.deps.json` / `.runtimeconfig.json` if present).

5. **F2 text window**
   - Immediately after `NETLOAD`, open the text screen (**F2**).
   - Healthy load prints the Idle confirmation (step 6 above).
   - If there is no confirmation **and** no `Core.Exporter.Civil3D.load.log`, the assembly never entered `Initialize` (security / TFM / file not loaded).
   - If `load.log` exists but no F2 message, load worked and the editor was unavailable — open a drawing and try again, or run `COREXSPIKE` anyway.

6. **Commands**
   - Registered names: **`COREXSPIKE`**, **`CSPIKE`** (Civil plugin); **`COREXPROBE`** (AutoCAD-only probe).
   - Unknown command after a confirmed load message → report that; do not keep retrying random names.

7. **ARXLOAD**
   - Not applicable. This is a managed `.dll`; use **`NETLOAD`** only.

## If it is still broken — send this back

1. Civil 3D year / build (About dialog).
2. Exact full path of the DLL you `NETLOAD`ed.
3. Whether `deps.json` and `runtimeconfig.json` sat next to that DLL.
4. `SECURELOAD` value.
5. Full **F2** text from just before `NETLOAD` through after `COREXSPIKE` / `COREXPROBE`.
6. Contents of `Core.Exporter.Civil3D.load.log` (and probe log if used), if the files exist.
7. Result of the AutoCAD-only probe (`COREXPROBE`) vs Civil plugin (`COREXSPIKE`).
