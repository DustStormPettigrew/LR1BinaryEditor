# Binary Editor presentation metadata audit

The semantic format registry is `LibLR1.Schema.SchemaStructureProvider.Formats`. Binary Editor does not add formats to that registry.

| Source | Classification | Retained use |
| --- | --- | --- |
| LibLR1 `SchemaStructureProvider` and generated schemas | Canonical format/schema information | Registered discovery, root types, encoding classification, token labels, help, unknown read/write policy |
| LibLR1 `format-evidence.json` | Canonical generated evidence metadata | Format-level matched 1999, portable, verified 2001, corpus-inferred, or unresolved status |
| `Util_Static_Resources.cs` descriptions | Binary Editor presentation metadata | Friendly picker/navigation names only; keys are required to be a subset of the LibLR1 registry |
| `blocks.cfg` | Binary Editor RE presentation metadata | Additional local block comments and highlighting hints; never routing or grammar |
| `properties.cfg` | Binary Editor RE presentation metadata | Additional local property comments and highlighting hints; never routing or grammar |
| syntax color JSON under `%APPDATA%` | User-configurable styling | AvaloniaEdit colors only |
| file-type icons | Binary Editor presentation metadata | Navigation and picker visuals only |

Canonical schema labels are shown first. A differing local comment is appended as `presentation:` so legacy RE notes remain visible without overriding LibLR1 metadata.
