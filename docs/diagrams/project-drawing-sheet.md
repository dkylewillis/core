# Project, Drawing, and Sheet Model

```mermaid
erDiagram
  PROJECT ||--o{ SOURCE_FILE : contains
  PROJECT ||--o{ DRAWING : includes
  PROJECT ||--o{ DOCUMENT : includes
  DRAWING ||--o{ XREF_INSTANCE : references
  DRAWING ||--o{ SOURCE_ENTITY : contains
  SOURCE_ENTITY ||--o{ ENTITY_MAPPING : supports
  DESIGN_OBJECT ||--o{ ENTITY_MAPPING : interprets
  DOCUMENT ||--o{ SHEET : publishes
  SHEET ||--o{ VIEWPORT : contains
  VIEWPORT ||--o{ PRESENTATION_INSTANCE : contains
  DESIGN_OBJECT ||--o{ PRESENTATION_INSTANCE : appears_on
```
