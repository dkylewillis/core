# RULE-002: `core.network-connectivity` (version 1)

Detects gravity and pressure network parts that are not connected as a network should be.

## Inputs

GravityNetwork, GravityPipe, GravityStructure, PressureNetwork, and pressure-part DesignObjects, and their native connection relationships. Each DesignObject is evaluated once, regardless of how many data-shortcut references map to it.

## Applicability

Native Civil 3D networks only. Utilities drawn as ordinary linework are out of scope for version 1.

## Parameters

| Parameter | Type | Default | Meaning |
| --- | --- | --- | --- |
| `reportStructuresWithoutPipes` | boolean | `true` | Report structures with no connected pipes |

## Outputs

| Code | Kind | Severity | Condition |
| --- | --- | --- | --- |
| `pipe-end-unconnected` | Finding | medium | A gravity pipe's start or end structure is null. One finding per unconnected end; `facts.end` is `start` or `end` |
| `structure-without-pipes` | Finding | medium | A gravity structure has no connected pipes (when enabled) |
| `connection-mismatch` | Finding | high | A pipe names a structure that does not list the pipe, or a structure lists a pipe that does not name it |
| `pressure-part-unconnected` | Finding | medium | A pressure pipe has a null start or end connection |

## Evidence

The part's name, handle, and network; the connection fields as recorded; and for unconnected ends, the end's plan coordinates.

## Not in version 1

- Topology for linework utilities (endpoint snapping within a tolerance), which would carry a `rule` basis.
- Flow-direction and slope checks; these belong to a separate hydraulic rule.
