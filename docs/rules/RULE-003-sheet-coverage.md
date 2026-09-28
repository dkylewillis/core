# RULE-003: `core.sheet-coverage` (version 1)

Detects design objects within a sheet's intended coverage that are not visible on that sheet.

## Inputs

Sheets, layouts, plan viewports and their world footprints, CoverageIntents (view frames), DesignObjects of the configured types, and PresentationInstances produced by [visibility resolution](visibility.md).

## Applicability

For each sheet:

1. **Coverage area.** If `useViewFrames` is true and one or more view frames name this sheet, the coverage area is the union of their boundaries. Otherwise it is the union of the world footprints of the sheet's plan viewports. A sheet with neither is skipped.
2. **Candidates.** DesignObjects whose type is in `objectTypes` and whose plan geometry intersects the coverage area. Point objects (structures) use their position; linear objects (pipes) use their plan centerline.

## Parameters

| Parameter | Type | Default | Meaning |
| --- | --- | --- | --- |
| `objectTypes` | string array | `["GravityPipe", "GravityStructure"]` | DesignObject types in scope |
| `useViewFrames` | boolean | `true` | Prefer view frames over viewport footprints as the coverage area |

## Outputs

| Code | Kind | Severity | Condition |
| --- | --- | --- | --- |
| `object-not-visible-on-sheet` | Finding | medium | A candidate is not visible in any plan viewport of the sheet whose footprint contains it |
| `object-outside-all-viewports` | Finding | medium | A candidate lies in a view-frame coverage area but inside no plan viewport footprint of the sheet |

An object is visible on the sheet when at least one of its SourceEntities, through at least one loaded Xref path, has a visible PresentationInstance in a plan viewport of the sheet.

## Evidence

The sheet number, the DesignObject, and for each plan viewport of the sheet: whether the object is inside, whether it is visible, the ordered reasons, and the effective layer.

## Basis

`native` when the DesignObject mapping and the coverage source (view frame or viewport) are native.
