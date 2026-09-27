# Coordinates and Transformations

CORE must make coordinate context explicit at every boundary. A point is not complete without its coordinate reference, units, and lineage.

## Coordinate spaces

- **Object space (OCS)** — the object coordinate system in which AutoCAD stores planar entities such as lightweight polylines, circles, and arcs, defined by the entity's normal and elevation.
- **Block space** — coordinates local to a block definition, placed by each block reference's transform. Block references may be nested.
- **Source space** — coordinates as authored in a source drawing's model space (its world coordinate system).
- **Drawing space** — the source drawing's model or layout context.
- **Xref space** — coordinates local to the referenced drawing.
- **Project/world space** — the normalized project coordinate system used for cross-drawing analysis.
- **Profile-view space** — model-space position within a Civil 3D profile view, which maps to station and elevation along an alignment with a vertical exaggeration. It does not map to plan coordinates.
- **Section-view space** — the equivalent for section views, mapping to offset and elevation at a station.
- **Sheet space** — paper coordinates on a published sheet.
- **Viewport space** — the view transform connecting model/world space to a sheet viewport.

## Rules

1. Store the transform, units, and coordinate reference metadata, not only transformed coordinates.
2. Preserve the complete Xref chain from an object to project/world space.
3. Preserve the complete block-reference chain from an entity in a block definition to its drawing.
4. Never flatten an Xref relationship in a way that prevents tracing back to the source instance.
5. Record precision, tolerance, and transformation version for derived geometry.
6. Treat unit conversion and coordinate reference conversion as explicit operations.
7. Treat a viewport that displays a profile or section view as showing that view's space, not plan space.

## Units and coordinate reference

Each drawing records:

- **Linear units** from `INSUNITS`. US survey feet and international feet are different units and must never be treated as interchangeable. At state-plane magnitudes the difference is several feet.
- **Coordinate system** — the Civil 3D drawing coordinate system code, when set. A drawing without one is recorded as having an unknown coordinate reference, not assumed to match the project.
- **Grid-to-ground scale** — the combined scale factor, when the project uses ground (surface) coordinates.

When a host and an Xref have different insertion units, the unit scaling applied by AutoCAD is part of the Xref transform and is recorded with it.

Most well-organized projects attach Xrefs at the origin with no rotation or scale, so the Xref transform is usually the identity. CORE still computes and stores it, and treats a non-identity transform as a fact worth surfacing (for example, a rotated survey base).

## Precision

- Coordinates are stored as 64-bit floats, or relative to a recorded local origin. 32-bit floats lose tenths of a foot at state-plane magnitudes.
- Spatial indexes may be approximate. SQLite R*Tree stores 32-bit floats by default; it is acceptable as a coarse index only if every geometric check re-tests candidates against exact geometry.
- Arcs, bulges, and spirals are kept as analytic curves. Any tessellation is derived geometry with a recorded tolerance.

See [ADR-010](docs/adr/ADR-010-units-and-precision.md).

## Conceptual transform

```text
entity in OCS
  -> block reference transform(s)
  -> source/Xref space
  -> Xref instance transform(s), including unit scaling
  -> drawing/project transform
  -> project/world space
  -> viewport transform
  -> sheet space
```

For profile views:

```text
station, elevation
  -> profile-view mapping (vertical exaggeration, datum, station range)
  -> model space of the drawing containing the profile view
  -> viewport transform
  -> sheet space
```

Cross-drawing geometric checks should operate in a common project/world space and retain the source-space geometry for evidence and review.
