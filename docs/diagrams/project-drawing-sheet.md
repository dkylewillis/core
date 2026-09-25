# Project, Drawing, and Sheet Model

```mermaid
erDiagram
  PROJECT ||--o{ SOURCE_FILE : contains
  PROJECT ||--o{ DRAWING : includes
  PROJECT ||--o{ DOCUMENT : includes
  DRAWING ||--o{ XREF_INSTANCE : references
  DRAWING ||--o{ DESIGN_OBJECT : contains
  DOCUMENT ||--o{ SHEET : publishes
  SHEET ||--o{ VIEWPORT : contains
  VIEWPORT }o--o{ DESIGN_OBJECT : displays
```
