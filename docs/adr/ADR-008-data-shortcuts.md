# ADR-008: Data Shortcuts Are First-Class References

- Status: Accepted
- Date: 2026-09-27

## Context

In Civil 3D projects, alignments, profiles, surfaces, and pipe networks usually live in a source drawing and are brought into design and sheet drawings through data shortcuts, not Xrefs. A project dependency graph built only from Xrefs misses these relationships, and the same object appears in several drawings.

## Decision

Data shortcuts are recorded as explicit `DataReference` relationships alongside `XrefInstance`. Discovery follows data shortcuts as well as Xrefs. A referenced object and its source object map to one DesignObject; the reference is preserved as its own SourceEntity with a link to the source.

## Consequences

- Duplicate-identity checks treat data-shortcut references as intentional and do not report them as duplicates.
- Broken or stale data shortcuts (missing source drawing, missing source object, or a reference that is out of date with its source) are reportable in the same way as broken Xrefs.
- The exporter must read shortcut metadata from each drawing and from the project's `_Shortcuts` folder.
