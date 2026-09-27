# Fixture: mini-site

A small, hand-authored COREX package that exercises every case the design promises, with expected import, presentation, and review results. It is the primary acceptance test for the importer and the first four rules.

`package/` is an unpacked `.corex`. `expected/` holds the results an implementation must produce. The JSON files are the source of truth; edit them directly and keep the scenario below in sync.

Class names (for example `AeccDbPipeLabel`) and handles are illustrative. The exporter spike will confirm real class names; that does not change the scenario.

## Project

Coordinates are state-plane magnitudes (around 2,150,000 E, 1,380,000 N, in feet) to exercise 64-bit precision.

```text
Sheet set MiniSite.dst
|-- C3.01  STORM DRAINAGE PLAN     C301.dwg, layout C301
`-- C5.01  STORM DRAINAGE PROFILE  C501.dwg, layout C501

C301.dwg (sheet)
|-- xref attach  C-BASE.dwg   saved path is stale; resolved by support search path
|   `-- xref attach  SURVEY.dwg   US survey feet, rotated 0.5 degrees, local coordinates
|-- xref attach  C-STRM.dwg
|   `-- xref overlay C-BASE.dwg   not loaded through C301
`-- xref attach  C-UTIL.dwg   not found

C501.dwg (sheet)
|-- dref  Alignment SW-A      -> C-ALIGN.dwg
|-- dref  Profile   SW-A FG   -> C-ALIGN.dwg
|-- dref  Network   STORM     -> C-STRM.dwg  (with all parts)
|-- dref  Surface   EG        -> C-SURF.dwg  source not found
`-- profile view SW-A PV (vertical exaggeration 10)
```

### Storm network STORM (C-STRM.dwg)

| Part | Handle | Notes |
| --- | --- | --- |
| DI-1 | 101 | Inlet; P-1 and P-4 connect |
| DI-2 | 102 | Inlet; style "Inlet Type C - Plan" draws it on `C-STRM-STRC-C` |
| MH-1 | 103 | Manhole; P-1, P-2, P-3 connect |
| OUT-1 | 104 | Headwall outfall |
| DI-3 | 105 | Inlet with no connected pipes |
| P-1 | 111 | 18 in RCP, DI-1 to MH-1, 0.71% |
| P-2 | 112 | 24 in RCP, DI-2 to MH-1, 0.71% |
| P-3 | 113 | 24 in RCP, MH-1 to OUT-1, 1.50% |
| P-4 | 114 | 12 in RCP, free upstream end, to DI-1 |
| Label | 120 | On P-1, computed text, not overridden |
| Label | 121 | On P-3, text overridden to `24" HDPE @ 1.50%` |

### Sheet C3.01 (C301.dwg)

- Plan viewport (handle `5E1`), 1" = 40', centered on MH-1. Frozen in the viewport: `C-STRM|C-STRM-STRC-C` and `C-BASE|C-WATR-HYD`.
- MLeader `5C1` points at P-2 and reads `18" RCP @ 0.50%` (wrong diameter and slope).
- MLeader `5C2` points at P-3 and reads `24" RCP @ 1.50%` (correct).
- C-BASE contains hydrant block `HYD` on layer `C-WATR-HYD`; the circle inside the block is on layer `0` and inherits the block reference's layer.

### Sheet C5.01 (C501.dwg)

- One viewport showing the profile view. It is classified as a profile viewport, so plan coverage does not apply.

## What each case tests

| Case | Where | Expected |
| --- | --- | --- |
| Broken Xref | C301 → C-UTIL | Finding `xref-not-found` |
| Stale saved path resolved by search path | C301 → C-BASE | Observation `xref-resolved-by-fallback` |
| Overlay not loaded through a parent | C-STRM → C-BASE | Path recorded, `loaded: false`; no finding |
| Mixed units and rotation in an Xref chain | C-BASE → SURVEY | Exact world coordinates; validation warning `mixed-units-in-xref-chain` |
| Block transform and layer 0 inheritance | C-BASE hydrant | World center; hidden by viewport freeze of the inherited layer |
| Data shortcuts map to one DesignObject | C501 references | 13 DesignObjects; each network, part, alignment, and profile maps from both its source and its reference |
| Broken data shortcut | C501 → EG | Finding `data-reference-source-not-found` |
| Unconnected pipe end | P-4 | Finding `pipe-end-unconnected` |
| Structure with no pipes | DI-3 | Finding `structure-without-pipes` |
| Style-driven visibility | DI-2 on C3.01 | Hidden; finding `object-not-visible-on-sheet` |
| Callout disagrees with model | MLeader 5C1 | Finding with diameter and slope mismatches, basis `rule` |
| Overridden label disagrees | Label 121 | Finding with material mismatch, basis `native` |
| Correct callout | MLeader 5C2 | No finding |
| Profile-view mapping | C501 profile view | Station/elevation maps to exact model coordinates |

## Expected files

All entity references use `{ "drawing": <drawing id>, "handle": <handle> }`. DesignObjects are referred to by name in this fixture because names are unique here.

### `expected/import.json`

- `drawings` — drawing ids and normalized linear units.
- `xrefPaths.paths` — every Xref path reachable from each sheet drawing: the drawings on the path, the Xref instances used, whether AutoCAD would load it, the reason when not (`overlay-not-nested`, `not-found`), and the 4×4 row-major world transform. Exhaustive.
- `dataReferences` — every data reference with its source and status. Exhaustive.
- `designObjects` — every DesignObject with its type, name, system, the SourceEntities it maps from (with method and whether through a data reference), and normalized properties. Exhaustive.
- `validation` — expected errors (none) and warnings, matched on code and subject. Exhaustive.
- `geometryChecks` — world coordinates, profile-view mapping, and viewport footprints that must match within `tolerance`.

### `expected/presentation.json`

Per viewport, a list of entries, each with the entity, the Xref path it is seen through, whether it is inside the viewport footprint, whether it is visible, the ordered `reasons` when it is not, and the effective host layer. Entries are not exhaustive; every listed entry must match. Reason codes and their precedence are defined in [docs/rules/visibility.md](../../../docs/rules/visibility.md).

### `expected/findings.json`

Findings and observations produced with [`profiles/default.rule-profile.json`](../../../profiles/default.rule-profile.json). They are exhaustive and are matched on `rule`, `code`, and `subject`; `severity`, `basis`, and every key in `facts` must also match. `mustNotReport` lists subjects that a rule must not report, with the reason.
