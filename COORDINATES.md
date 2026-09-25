# Coordinates and Transformations

CORE must make coordinate context explicit at every boundary. A point is not complete without its coordinate reference, units, and lineage.

## Coordinate spaces

- **Source space** — coordinates as authored in a source drawing.
- **Drawing space** — the source drawing's model or layout context.
- **Xref space** — coordinates local to the referenced drawing.
- **Project/world space** — the normalized project coordinate system used for cross-drawing analysis.
- **Sheet space** — paper coordinates on a published sheet.
- **Viewport space** — the view transform connecting model/world space to a sheet viewport.

## Rules

1. Store the transform, units, and coordinate reference metadata, not only transformed coordinates.
2. Preserve the complete Xref chain from an object to project/world space.
3. Never flatten an Xref relationship in a way that prevents tracing back to the source instance.
4. Record precision, tolerance, and transformation version for derived geometry.
5. Treat unit conversion and coordinate reference conversion as explicit operations.

## Conceptual transform

```text
object in Xref space
  -> Xref instance transform(s)
  -> drawing/project transform
  -> project/world space
  -> viewport transform
  -> sheet space
```

Cross-drawing geometric checks should operate in a common project/world space and retain the source-space geometry for evidence and review.
