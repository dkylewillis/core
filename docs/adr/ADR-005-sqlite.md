# ADR-005: SQLite as Initial Storage

- Status: Accepted
- Date: 2026-09-25

SQLite is the initial local storage layer because it supports relational metadata, transactions, indexes, reproducible workflows, and low operational burden. The schema must be versioned; large source payloads may remain in files referenced by manifests and checksums.
