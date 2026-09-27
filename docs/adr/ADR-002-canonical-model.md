# ADR-002: Source-Independent Canonical Model

- Status: Accepted

CORE normalizes imported information into a source-independent canonical model while retaining source identity and provenance, so rules and review workflows do not depend on one source format.

The canonical model is created before QC runs. SourceEntities preserve extracted source facts; DesignObjects add engineering meaning through provenance-bearing mappings and do not duplicate source geometry or native properties unless CORE derives new information.
