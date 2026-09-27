# ADR-009: Stable Identity and a Separate Review Store

- Status: Accepted
- Date: 2026-09-27

## Context

Projects are reviewed repeatedly: submittal 1 is reviewed and findings are dispositioned, then submittal 2 arrives as a new `.corex` and a new `.core`. If findings live inside the snapshot they were produced from, every re-import loses the engineer's dispositions or brings dismissed findings back. Carrying findings forward requires identities that survive re-export.

## Decision

1. **`.core` is an immutable canonical snapshot.** It is written once from one `.corex` after validation and is never modified by review.
2. **Review data lives in a separate review store (`.corereview`).** Rule runs, observations, findings, evidence, and dispositions reference snapshots by checksum and entities by stable identity.
3. **Stable identity:**
   - Drawing: `Database.FingerprintGuid`, with path as a secondary key.
   - SourceEntity: drawing identity plus native handle.
   - DesignObject: the stable identities of its mapped SourceEntities plus its type.
4. **Carrying findings forward.** When a rule runs against a new snapshot, each new finding is matched to earlier findings:
   - An exact identity match inherits the earlier disposition history.
   - A fallback match (same type and name, location within tolerance) is recorded with its method, and the finding is flagged for confirmation rather than silently inheriting a dismissal.
   - Earlier findings with no match in the new snapshot are marked resolved-by-change, not deleted.

## Consequences

- Snapshot-to-snapshot differences (objects added, removed, or changed) can be reported directly.
- A reviewer can share a `.core` and a `.corereview` together, or share only the `.core` to let someone else review independently.
- Handles do not survive WBLOCK, copy and paste, or redrawing, so fallback matching will always be needed and must be visible to the engineer.
