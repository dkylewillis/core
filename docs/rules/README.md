# Rule Specifications

These specifications are normative for the first build. Each rule is versioned; changing a rule's behavior increments its version and updates the [mini-site fixture](../../fixtures/corex/mini-site/) expectations.

| Rule | Version | Spec |
| --- | --- | --- |
| `core.source-reference` | 1 | [RULE-001](RULE-001-source-reference.md) |
| `core.network-connectivity` | 1 | [RULE-002](RULE-002-network-connectivity.md) |
| `core.sheet-coverage` | 1 | [RULE-003](RULE-003-sheet-coverage.md) |
| `core.annotation-vs-model` | 1 | [RULE-004](RULE-004-annotation-vs-model.md) |

Presentation and visibility resolution, which RULE-003 and the Document Model depend on, is defined in [visibility.md](visibility.md).

## Rule contract

Every rule declares:

- **Inputs** — the canonical entities it reads. The result's basis is computed only from declared inputs.
- **Applicability** — which subjects it evaluates.
- **Parameters** — read from the [rule profile](../../profiles/default.rule-profile.json); rules contain no hard-coded thresholds.
- **Units and tolerances** — stated explicitly.
- **Outputs** — the finding and observation codes it can produce.
- **Evidence** — what each output must carry.

A rule reads only the canonical snapshot and its parameters. It never reads `.corex`, modifies the snapshot, or depends on the output of another rule.

## Outputs

### Finding

```json
{
  "rule": "core.network-connectivity",
  "ruleVersion": 1,
  "code": "pipe-end-unconnected",
  "severity": "medium",
  "basis": "native",
  "subject": { "drawing": "C-STRM", "handle": "114" },
  "title": "Pipe P-4 has an unconnected start",
  "facts": { "pipe": "P-4", "end": "start", "point": [2150000.0, 1380300.0] },
  "evidence": [
    { "role": "subject", "target": { "kind": "design-object", "key": "..." }, "values": { "startStructure": null } }
  ]
}
```

### Observation

Same shape without `severity`. An observation records a fact the engineer may want to see but that is not asserted as an issue.

### Severity

| Severity | Meaning |
| --- | --- |
| `high` | Likely error in the construction documents, or a broken reference that makes part of the project unreviewable. |
| `medium` | Likely issue that needs engineer attention. |
| `low` | Minor issue worth recording. |

### Basis

The least-certain mapping method among the declared inputs actually used for this result: `native`, `rule`, or `ai`. A link established by spatial proximity or text parsing is a `rule` input. See the [data model](../../DATA_MODEL.md#basis).

### Subject and finding key

The subject is the primary SourceEntity of the thing being reported. The **finding key** used to carry findings across snapshots ([ADR-009](../adr/ADR-009-identity-and-review-lifecycle.md)) is `rule + code + stable identity of subject`, plus the sheet number for sheet-scoped rules.

### Evidence

Each evidence item has a `role` (`subject`, `related`, `measurement`, `threshold`), a `target` (`source-entity`, `design-object`, `xref-instance`, `data-reference`, `viewport`, `layout`, `sheet`, `document-region`) and the values that support the result. A finding without the evidence its rule requires is not emitted.
