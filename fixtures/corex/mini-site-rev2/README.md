# Fixture: mini-site-rev2

The second submittal of [mini-site](../mini-site/). It is the acceptance test for reviewing across submittals ([ADR-009](../../../docs/adr/ADR-009-identity-and-review-lifecycle.md)): carrying findings and dispositions forward, fallback matching, resolved-by-change, and snapshot diffs.

## Changes from revision 1

| Change | Drawing | Effect |
| --- | --- | --- |
| MLeader `5C1` corrected to `24" RCP @ 0.71%` | C301 | Revision 1 annotation finding is resolved by change |
| New pipe P-5 (`115`) connects DI-3 to MH-1 | C-STRM | Revision 1 `structure-without-pipes` on DI-3 is resolved by change |
| P-4 erased and redrawn: handle `114` becomes `116`, same name and geometry | C-STRM | Finding carried by **fallback** match; needs confirmation |
| C-UTIL still missing, DI-2 still hidden, EG still broken, label 121 still overridden | — | Findings carried by exact match |

`fingerprintGuid` is unchanged for every drawing; `versionGuid` changes for C-STRM, C301, and C501.

## Test procedure

1. Import `mini-site/package` and run all rules (revision 1).
2. Apply the dispositions in `expected/rev1-dispositions.json`.
3. Import `mini-site-rev2/package` and run all rules; compare with `expected/findings.json`.
4. Carry from revision 1 to revision 2; compare with `expected/carry.json`.
5. Diff the two snapshots; compare with `expected/carry.json` → `diff`.
