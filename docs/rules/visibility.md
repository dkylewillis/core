# Presentation and Visibility Resolution

For each model-space viewport, the importer creates PresentationInstances that say whether a SourceEntity, seen through a particular Xref path, is inside the viewport and whether it is visible. This is canonical-model creation, not QC; rules read the result.

## Viewport classification

- The layout's paper-space view (`isPaperSpaceView`) is never evaluated.
- A model viewport is a **profile viewport** when its world footprint contains the model-space extents of a profile view; otherwise it is a **plan viewport**. Only plan viewports are used for plan coverage.

## Footprint

The world footprint of a plan viewport is its paper rectangle (or nonrectangular clip boundary) mapped through the inverse viewport transform: paper point `p` maps to DCS as `(p - centerPaper) / customScale + view.center`, then DCS to WCS using `view.target`, `view.direction`, and `view.twist`. An entity is **inside** when any part of its plan geometry intersects the footprint.

## Xref paths

Entities in an Xref'd drawing are evaluated once per Xref path from the viewport's drawing. A path is **not loaded** when any instance on it is unresolved, not found, or unloaded, or when an overlay is nested inside another Xref (overlays load only in the drawing that directly references them).

## Effective layer

1. Start with the entity's own layer.
2. If the layer is `0` and the entity is inside a block definition, use the effective layer of the block reference (recursively for nested blocks).
3. If the layer is `0` at the top level of an Xref'd drawing, use the layer the Xref is inserted on.
4. For a Civil 3D object with a style, visibility is evaluated per display component for the viewport's view direction (`plan` for plan viewports). A component on layer `0` uses the object's effective layer; otherwise it uses its own layer. The object is visible if any component is visible.
5. For an entity seen through an Xref, the host layer name is `<Xref block name of the drawing that owns the layer>|<layer>`, for example `C-STRM|C-STRM-STRC-C` or `SURVEY|V-CTRL` for a nested Xref.

Layer state comes from the viewport's drawing: its Xref-dependent layer records when `VISRETAIN` is true, otherwise the Xref drawing's own layer table. Viewport freeze comes from the viewport's `frozenLayers`.

## Reason codes and precedence

When an entity is inside the footprint but not visible, `reasons` lists every applicable code in this order:

| Order | Code | Meaning |
| --- | --- | --- |
| 1 | `xref-not-loaded` | The Xref path is not loaded |
| 2 | `viewport-off` | The viewport is turned off |
| 3 | `clipped-by-viewport` | Outside a nonrectangular viewport clip |
| 4 | `clipped-by-xref` | Outside an Xref or block clip boundary (or inside an inverted one) |
| 5 | `xref-insert-layer-hidden` | An Xref on the path is inserted on a layer that is off or frozen |
| 6 | `entity-invisible` | The entity's own visibility property is off |
| 7 | `style-hides-object` | No display component of the style is visible for the view direction |
| 8 | `layer-off` | Effective layer is off |
| 9 | `layer-frozen` | Effective layer is frozen |
| 10 | `layer-frozen-in-viewport` | Effective layer is frozen in this viewport |
| 11 | `style-component-layer-off` | Every visible component's layer is off |
| 12 | `style-component-layer-frozen` | Every visible component's layer is frozen |
| 13 | `style-component-layer-frozen-in-viewport` | Every visible component's layer is frozen in this viewport |

Codes 8–10 apply when the visibility decision rests on the object's effective layer (no style, or components on layer `0`); codes 11–13 apply when it rests on a component's own layer.

An entity outside the footprint has `inside: false` and no reasons.
