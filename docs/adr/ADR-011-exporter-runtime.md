# ADR-011: Civil 3D Exporter Runtime and Version Support

- Status: Proposed — pending an extraction spike on a representative project
- Date: 2026-09-27

## Context

The exporter must run inside Autodesk software to use the AutoCAD and Civil 3D .NET APIs. AutoCAD and Civil 3D 2025 and later run on .NET 8; 2024 and earlier run on .NET Framework 4.8. Civil 3D objects inside an Xref cannot be queried through the host drawing, so each drawing must be read from its own database. Some Civil 3D APIs are known to behave differently on side databases (opened with `Database.ReadDwgFile`) than on documents open in the editor.

## Decision

- The exporter extracts each drawing from its own database and records Xref and data-shortcut relationships between them.
- It supports an explicitly listed range of Civil 3D versions, with a separate build per .NET runtime.
- It supports two modes: interactive (a command inside Civil 3D) and batch (AutoCAD Core Console with Civil 3D loaded, `accoreconsole.exe /product C3D`).

## Before acceptance

Run an extraction spike on one real project that uses nested Xrefs and data shortcuts. Confirm, for each initial object type, whether side-database reads return complete data (including surfaces, styles, labels, and pressure networks), or whether drawings must be opened as documents. Record the supported version range.

## Consequences

The COREX manifest records the exporter version and the Autodesk product and version used for each export, so extraction differences between versions are traceable.
