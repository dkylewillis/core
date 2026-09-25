# Initial QC Rules

The first rule set should be small, deterministic, explainable, and testable against a representative project.

## Initial candidates

| Rule | Purpose | Evidence |
|---|---|---|
| Missing or invalid source reference | Detect broken, unresolved, or stale Xrefs and source links | File path, checksum, resolution status |
| Duplicate or conflicting object identity | Detect objects that cannot be uniquely reconciled | Source IDs, locations, attributes |
| Cross-drawing clearance | Compare compatible utility/design geometries in project space | Both objects, transforms, measured clearance, threshold |
| Network connectivity | Detect disconnected or impossible network elements | Connected objects, endpoints, topology result |
| Sheet/viewport coverage | Detect design content absent from intended sheet views | Sheet, viewport, extents, scale |
| Attribute completeness | Detect required engineering attributes that are missing or invalid | Object fields, rule requirement |

## Rule contract

Each rule should declare its inputs, applicability conditions, units, tolerance, version, execution result, and evidence requirements. A rule may produce no issue, an observation needing review, or a finding with severity and status.

## Deterministic-first policy

Deterministic rules run before AI. AI may summarize, classify, prioritize, or suggest follow-up questions using available evidence, but it must not erase deterministic results or create an unsupported finding.
