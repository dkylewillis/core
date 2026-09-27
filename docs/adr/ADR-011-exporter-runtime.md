# ADR-011: Civil 3D Exporter Runtime and Version Support

- Status: Proposed — pending the [exporter spike](../EXPORTER_SPIKE.md) on a representative project
- Date: 2026-09-27

## Context

The exporter must run inside Autodesk software to use the AutoCAD and Civil 3D .NET APIs. Each Autodesk release requires a specific .NET runtime:

| Civil 3D / AutoCAD | Managed runtime | Target framework |
| --- | --- | --- |
| 2020–2024 | .NET Framework 4.8 | `net48` |
| 2025–2026 | .NET 8 | `net8.0-windows` |
| 2027 | .NET 10 | `net10.0-windows` |

Civil 3D objects inside an Xref cannot be queried through the host drawing, so each drawing must be read from its own database. Some Civil 3D APIs are known to behave differently on side databases (opened with `Database.ReadDwgFile`) than on documents open in the editor.

## Decision

- The exporter extracts each drawing from its own database and records Xref and data-shortcut relationships between them.
- Initial supported range: **Civil 3D 2025, 2026, and 2027**, built from one code base that multi-targets `net8.0-windows` and `net10.0-windows`. Civil 3D 2024 and earlier (`net48`) are out of scope unless the spike shows a user need.
- Autodesk references come from the official NuGet packages (`AutoCAD.NET` and `Civil3D.NET`) with `ExcludeAssets="runtime"`, so the exporter builds without a local ObjectARX SDK.
- The exporter supports two modes: interactive (a command inside Civil 3D) and batch (AutoCAD Core Console with Civil 3D loaded, `accoreconsole.exe /product C3D`).
- The exporter writes COREX through the shared `Core.Corex` contract library ([ADR-012](ADR-012-implementation-stack.md)), which targets `netstandard2.0` and is usable from every supported runtime.

## Before acceptance

Run the [exporter spike](../EXPORTER_SPIKE.md) on one real project that uses nested Xrefs and data shortcuts. Confirm, for each initial object type, whether side-database reads return complete data (including surfaces, styles, labels, and pressure networks), or whether drawings must be opened as documents. Confirm the supported version range.

## Consequences

The COREX manifest records the exporter version and the Autodesk product, version, and runtime used for each export, so extraction differences between versions are traceable. The exporter is built and tested on Windows with Civil 3D installed; it cannot be built or tested by a Linux cloud agent beyond compiling against the NuGet reference assemblies.
