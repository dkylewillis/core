# RULE-001: `core.source-reference` (version 1)

Detects Xrefs and data-shortcut references that are broken, or that resolve only by fallback and may break on another machine.

## Inputs

XrefInstances, DataReferences, and the shortcut list from the snapshot. All are native source facts.

## Applicability

- Every Xref instance placed directly in any drawing of the snapshot.
- Every data-shortcut reference object in any drawing.
- Every shortcut in the project's shortcut folder.

Overlays that are not loaded through a parent are normal AutoCAD behavior and are not evaluated through that path; the overlay instance itself is evaluated once, in the drawing that directly references it.

## Parameters

| Parameter | Type | Default | Meaning |
| --- | --- | --- | --- |
| `reportFallbackResolution` | boolean | `true` | Emit an observation when an Xref resolved by something other than its saved path |
| `unloadedXref` | `finding` \| `observation` \| `ignore` | `observation` | How to report intentionally unloaded Xrefs |

## Outputs

| Code | Kind | Severity | Condition |
| --- | --- | --- | --- |
| `xref-not-found` | Finding | high | Xref status `not-found` |
| `xref-unresolved` | Finding | high | Xref status `unresolved` (found but failed to load) |
| `xref-unloaded` | per parameter | medium | Xref status `unloaded` |
| `xref-resolved-by-fallback` | Observation | — | Status `resolved` and `resolvedBy` is not `saved-path` or `relative-to-host` |
| `data-reference-source-not-found` | Finding | high | Reference status `source-not-found` |
| `data-reference-object-not-found` | Finding | high | Reference status `object-not-found` |
| `data-reference-out-of-date` | Finding | medium | Reference status `out-of-date` |
| `shortcut-invalid-unreferenced` | Observation | — | A shortcut with status other than `valid` that no drawing references |

Xref instances with status `unreferenced` are ignored.

## Evidence

- Xref: host drawing, Xref block name, saved path, resolved path, `resolvedBy`, status.
- Data reference: host drawing, shortcut name, object type, source path, source drawing and handle when known, status.

## Not in version 1

Detecting an Xref or source drawing that changed after the host was last saved (staleness by checksum or `VersionGuid`) needs extraction support that the exporter spike has not yet confirmed.
