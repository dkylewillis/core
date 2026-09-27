# Exporter Spike

A short, throwaway investigation on a real Civil 3D project to confirm the assumptions in [COREX.md](COREX.md) and [ADR-011](adr/ADR-011-exporter-runtime.md) before the exporter is designed. It must run on Windows with Civil 3D installed, so the project owner (or an agent on the owner's machine) runs it.

## Setup

- Civil 3D 2026 (and 2027 if available).
- A plugin project referencing the `AutoCAD.NET` and `Civil3D.NET` NuGet packages with `ExcludeAssets="runtime"`, targeting `net8.0-windows` for 2026 and `net10.0-windows` for 2027.
- A representative project with nested Xrefs, at least one overlay, data shortcuts (alignment, profile, surface, pipe network), a sheet set, and at least one plan and one plan/profile sheet. Keep it outside this repository if it contains client data.

## Questions to answer

Record each answer (yes/no, API used, notes) in a results table and attach it to ADR-011.

### Opening drawings

1. Can every drawing be read with `Database.ReadDwgFile` as a side database, without opening it in the editor?
2. On a side database, do these return complete data: pipe networks and parts, pressure networks, surfaces (`GetTriangles` or equivalent), alignments and their entities, profiles and PVIs, profile views, styles and display components, labels, view frames?
3. Which of the above require the drawing to be open as a document? Is `CivilDocument.GetCivilDocument(db)` usable on a side database?
4. Does the same code run in the AutoCAD Core Console with `accoreconsole.exe /product C3D`? Which parts fail?

### Identity and references

5. Are `Database.FingerprintGuid` and `Database.VersionGuid` populated for every drawing? Does copying a DWG keep the fingerprint?
6. For each Xref: attachment type, saved path, resolved path, how it was resolved, status, insertion layer, block transform, and clip boundary. Is the unit conversion included in the block reference's scale factors?
7. For each data-shortcut reference object: how is it identified as a reference (`IsReferenceObject` or equivalent), and can the source drawing, source object handle, and shortcut name be obtained? How is an out-of-date or broken reference detected?
8. Can the `_Shortcuts` folder be enumerated through the API, or must its XML files be read directly?
9. Can the sheet set (`.dst`) be read through the Sheet Set Manager API to get sheet numbers, titles, and layout references?

### Display and visibility

10. For a Civil 3D object style: can each display component's name, view direction, visibility, and layer be read? Same for label styles.
11. For a viewport: frozen layers, view center, target, direction, height, custom scale, twist, clip boundary, and on/off state.
12. Is `VISRETAIN` readable, and are the host's Xref-dependent layer states available on the host database?

### Values and geometry

13. Pipe inner diameter, start and end points (centerline?), material, part family, and part size; structure position, rim, sump, and connected pipes.
14. Profile view: can the station/elevation-to-model mapping be read directly, or must it be sampled with `FindXYAtStationAndElevation`? Is it linear?
15. Label displayed text: is the rendered text of a Civil 3D label available through the API, and is there a way to tell whether it is overridden?
16. RXClass names for each object kind, to confirm or correct the illustrative names in the fixture.

### Scale

17. Export time and package size for the representative project, and for its largest surface.

## Output

- The results table, attached to ADR-011, which then moves to Accepted (or is amended).
- Corrections to `schemas/corex/` and the fixtures for anything the spike disproves, made as a normal pull request.
- The supported Civil 3D version range.
