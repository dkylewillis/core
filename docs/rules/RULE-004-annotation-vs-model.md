# RULE-004: `core.annotation-vs-model` (version 1)

Detects pipe callouts whose stated size, material, slope, or length disagrees with the pipe they describe.

## Inputs

Annotations (MLeaders and Civil 3D labels), GravityPipe DesignObjects, plan viewports and their transforms.

## Applicability

- **MLeaders** in model space or paper space of any drawing. The arrowhead (first leader vertex) links the callout to a pipe.
- **Civil 3D pipe labels** whose text is overridden. Labels with computed text are consistent with the model by construction and are skipped.
- Plain MText and Text without a leader are skipped in version 1; they have no reliable link to an object.
- An annotation is evaluated once per SourceEntity, even when it is visible on several sheets through Xrefs.

Text is taken from the annotation's plain text (formatting codes removed). Text that does not match the callout grammar is ignored.

## Linking

| Annotation | Link method | Basis contribution |
| --- | --- | --- |
| Civil 3D label | `label-annotates` — the label's native `annotates` reference | `native` |
| MLeader in paper space | `leader-proximity` — map the arrowhead through the plan viewport that contains it to world coordinates, then find pipes whose plan centerline is within `linkTolerancePaper / customScale` | `rule` |
| MLeader in model space | `leader-proximity` — as above, with tolerance `linkToleranceModelFt` | `rule` |

Exactly one pipe within tolerance links the callout. None produces the observation `callout-unlinked`; more than one produces `callout-ambiguous`.

## Callout grammar

Case-insensitive. Whitespace between tokens is optional unless shown.

```ebnf
callout   = [ length , ws ] , diameter , ws , material , [ ws? , slope ] ;
length    = number , ws? , ( "LF" | "'" ) , [ ws , "OF" ] ;
diameter  = number , ws? , ( '"' | "IN" | "INCH" ) ;
material  = identifier ;                 (* resolved through materialCodes *)
slope     = ( "@" | "AT" | "S=" ) , ws? , number , ws? , "%" ;
number    = digit , { digit } , [ "." , digit , { digit } ] ;
ws        = " " , { " " } ;
```

Examples: `18" RCP @ 0.50%`, `125 LF 24" HDPE @ 1.00%`, `24 IN RCP S=1.5%`.

## Comparison

| Field | Model value | Match when |
| --- | --- | --- |
| Diameter | Inner diameter in inches | `abs(stated - model) <= diameterToleranceIn` |
| Material | Pipe material mapped through `materialCodes` | Codes are equal. An unmapped model material produces the observation `material-unmapped` instead of a mismatch |
| Slope | `(startInvert - endInvert) / length2d * 100`, where invert = centerline elevation − inner diameter / 2 | `abs(stated - model) <= 0.5 × 10^(-d)`, where *d* is the number of decimals stated |
| Length | 2D length between pipe start and end points | `abs(stated - model) <= lengthToleranceFt` |

## Parameters

| Parameter | Type | Default |
| --- | --- | --- |
| `linkTolerancePaper` | number (paper inches) | `0.10` |
| `linkToleranceModelFt` | number | `4.0` |
| `diameterToleranceIn` | number | `0.01` |
| `lengthToleranceFt` | number | `1.0` |
| `materialCodes` | object mapping model material to code | see the default profile |

## Outputs

| Code | Kind | Severity | Condition |
| --- | --- | --- | --- |
| `annotation-value-mismatch` | Finding | high if diameter or material mismatch; otherwise medium | One or more fields disagree. One finding per annotation, listing every mismatch |
| `callout-unlinked` | Observation | — | Parsed callout with no pipe within tolerance |
| `callout-ambiguous` | Observation | — | Parsed callout with more than one pipe within tolerance |
| `material-unmapped` | Observation | — | Model material has no entry in `materialCodes` |

## Evidence

The annotation (drawing, handle, text, and whether overridden), the link method and distance, the linked pipe, and for each compared field the stated value, model value, units, and tolerance.
