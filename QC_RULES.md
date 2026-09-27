# Initial QC Rules

The first rule set should be small, deterministic, explainable, and testable against a representative project.

## Initial candidates

| Rule | Purpose | Evidence |
|---|---|---|
| Missing or invalid source reference | Detect broken, unresolved, unloaded, or stale Xrefs, data shortcuts, and source links | Saved and resolved path, checksum, resolution status |
| Duplicate or conflicting object identity | Detect objects that cannot be uniquely reconciled | Source IDs, locations, attributes |
| Cross-drawing clearance | Compare compatible utility/design geometries in project space | Both objects, transforms, measured clearance, threshold |
| Network connectivity | Detect disconnected or impossible network elements | Connected objects, endpoints, topology result |
| Sheet/viewport coverage | Detect design content absent from intended sheet views | Sheet, viewport, extents, scale, visibility reason |
| Attribute completeness | Detect required engineering attributes that are missing or invalid | Object fields, rule requirement |
| Annotation vs. model | Detect notes and labels whose stated values disagree with the model | Annotation text, parsed value, DesignObject value, units |

### Rule notes

- **Missing or invalid source reference** covers data shortcuts as well as Xrefs: a missing source drawing, a missing source object, or a reference out of date with its source. A path that resolves only on the author's machine is reported with both the saved and resolved paths.
- **Duplicate or conflicting object identity** must not report a data-shortcut reference and its source as duplicates, or a drawing that is Xref'd more than once as duplicated content. Copied drawings that share a `FingerprintGuid` are reported.
- **Cross-drawing clearance** defines two measurements separately: horizontal separation in plan, and vertical separation at crossings (for example, water–sewer separation criteria). Vertical separation requires 3D pipe geometry: centerline elevation and pipe size to derive invert and crown. Linework without elevation can only support the horizontal check. Thresholds come from the RuleProfile, not from the rule code.
- **Network connectivity** uses native connections for Civil 3D gravity networks (each pipe records its start and end structures) and pressure networks (pipes, fittings, appurtenances). Utilities drawn as ordinary linework use endpoint topology within a stated tolerance, and their findings carry a `rule` basis.
- **Sheet/viewport coverage** takes the intended coverage from Civil 3D view frames and match lines where they exist. An object inside a viewport that is not visible (frozen layer, hidden style component, clip, overlay) is reported with the specific reason.
- **Annotation vs. model** compares hand-entered MText, leaders, and tables with the objects they describe (for example, a note reading `18" RCP` on a 24-inch pipe). Civil 3D labels computed from the object are consistent by construction and are checked only when their text is overridden.

## Rule contract

Each rule should declare its inputs, applicability conditions, units, tolerance, version, parameters, execution result, and evidence requirements. A rule may produce no issue, an observation needing review, or a finding with severity and status.

Each execution is recorded as a RuleRun with the rule version, RuleProfile, parameter values, tolerances, and snapshot checksum. Rerunning the same rule version with the same profile against the same snapshot produces the same results.

Every observation and finding records its basis (`native`, `rule`, or `ai`), taken from the least-certain mapping among its inputs. See the [data model](DATA_MODEL.md#basis).

## Rule profiles

Engineering thresholds such as minimum cover, minimum and maximum slope, minimum velocity, and utility separation differ by jurisdiction and project. They are stored in a RuleProfile that names its source standard, not hard-coded in rules. A finding reports the profile and parameter values it used.

## Deterministic-first policy

Deterministic rules run before AI. AI may summarize, classify, prioritize, or suggest follow-up questions using available evidence, but it must not erase deterministic results or create an unsupported finding.
