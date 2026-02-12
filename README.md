# SchemaTools

MSBuild tasks for SQL Server schema metadata generation, validation, and documentation.

[![NuGet](https://img.shields.io/nuget/v/SchemaTools.svg)](https://www.nuget.org/packages/SchemaTools/)
[![Licence: MIT](https://img.shields.io/badge/Licence-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

## Features

- **Metadata extraction** — parses `CREATE TABLE` definitions via ScriptDom; outputs structured JSON covering columns, constraints, indexes, foreign keys, defaults, computed columns, and identity specifications
- **Pattern detection** — soft delete, polymorphic relationships, append-only tables, temporal versioning; all column names are configurable
- **Trigger generation** — hard-delete triggers for soft-delete tables; custom overrides respected; uses actual PK and active column names
- **Schema validation** — FK referential integrity, circular FK detection, `snake_case` naming conventions, temporal structure, audit columns, polymorphic structure, primary key presence, unique constraint validity; per-table overrides supported
- **Documentation** — Markdown with Mermaid ER diagrams, category grouping, statistics, constraints, and indexes
- **MSBuild integration** — runs before build; configurable via MSBuild properties and JSON; supports explicit file lists or directory scanning

## Getting Started

### 1. Install

```bash
dotnet add package SchemaTools
```

### 2. Configure

Create `schema-tools.json` in the project root. All settings are optional; sensible defaults apply:

```json
{
  "database": "MyDatabase",
  "defaultSchema": "dbo",
  "sqlServerVersion": "Sql160",

  "features": {
    "enableSoftDelete": true,
    "enableTemporalVersioning": true,
    "generateHardDeleteTriggers": true,
    "detectPolymorphicPatterns": true,
    "detectAppendOnlyTables": true
  },

  "validation": {
    "validateForeignKeys": true,
    "validatePolymorphic": true,
    "validateTemporal": true,
    "validateAuditColumns": true,
    "enforceNamingConventions": true,
    "treatWarningsAsErrors": false
  },

  "documentation": {
    "enabled": true,
    "includeErDiagrams": true,
    "includeStatistics": true,
    "includeConstraints": true,
    "includeIndexes": false
  },

  "categories": {
    "core": "Core entities",
    "reference": "Reference data"
  },

  "columns": {
    "active": "active",
    "createdAt": "created_at",
    "createdBy": "created_by",
    "updatedBy": "updated_by",
    "validFrom": "valid_from",
    "validTo": "valid_to",
    "auditForeignKeyTable": "individuals",
    "polymorphicPatterns": [
      { "typeColumn": "owner_type", "idColumn": "owner_id" },
      { "typeColumn": "account_type", "idColumn": "account_id" }
    ]
  },

  "overrides": {
    "audit_log": {
      "validation": { "validateAuditColumns": false }
    },
    "category:reference": {
      "features": { "enableSoftDelete": false }
    },
    "staging_*": {
      "features": { "detectAppendOnlyTables": false },
      "validation": { "enforceNamingConventions": false }
    }
  }
}
```

### 3. Build

```bash
dotnet build MyDatabase.sqlproj
```

Four targets run in sequence before build:

1. **SchemaToolsGenerateMetadata** — parses `Schema\Tables\*.sql` → `Build\schema-metadata.json`
2. **SchemaToolsValidate** — validates the generated metadata
3. **SchemaToolsGenerateTriggers** — writes triggers to `Schema\Triggers\_generated\`
4. **SchemaToolsGenerateDocs** — writes documentation to `Docs\SCHEMA.md`

## Configuration Reference

### Features

| Setting | Default | Description |
| --- | --- | --- |
| `enableSoftDelete` | `true` | Detect soft-delete pattern (temporal + `active` column) |
| `enableTemporalVersioning` | `true` | Detect and validate temporal tables |
| `generateHardDeleteTriggers` | `true` | Generate hard-delete triggers for soft-delete tables |
| `detectPolymorphicPatterns` | `true` | Detect polymorphic relationships (`owner_type`/`owner_id`) |
| `detectAppendOnlyTables` | `true` | Detect append-only tables (has `created_at`, no `updated_by`, non-temporal) |

### Validation

| Setting | Default | Description |
| --- | --- | --- |
| `validateForeignKeys` | `true` | Validate FK references exist across all tables |
| `validatePolymorphic` | `true` | Validate polymorphic table structure and metadata |
| `validateTemporal` | `true` | Validate temporal columns (`valid_from`/`valid_to`) and history table |
| `validateAuditColumns` | `true` | Validate `created_by`/`updated_by` presence |
| `enforceNamingConventions` | `true` | Enforce `snake_case` naming for tables, columns, FKs, and PKs |
| `treatWarningsAsErrors` | `false` | Treat validation warnings as build errors |

The following validations always run regardless of configuration: primary key presence, circular FK detection, soft-delete consistency, and unique constraint column validity.

### Documentation

| Setting | Default | Description |
| --- | --- | --- |
| `enabled` | `true` | Generate Markdown documentation |
| `includeErDiagrams` | `true` | Include Mermaid ER diagrams per category |
| `includeStatistics` | `true` | Include schema statistics summary |
| `includeConstraints` | `true` | Include constraint details per table |
| `includeIndexes` | `false` | Include index details per table |

### Columns

Column names used for pattern detection. All comparisons are case-insensitive.

| Setting | Default | Description |
| --- | --- | --- |
| `active` | `"active"` | Soft-delete flag column |
| `createdAt` | `"created_at"` | Append-only timestamp column |
| `createdBy` | `"created_by"` | Audit column: creator |
| `updatedBy` | `"updated_by"` | Audit column: last updater |
| `validFrom` | `"valid_from"` | Temporal period start column |
| `validTo` | `"valid_to"` | Temporal period end column |
| `auditForeignKeyTable` | `"individuals"` | Table that audit columns (`createdBy`/`updatedBy`) reference as FK |
| `polymorphicPatterns` | (see below) | Array of type/ID column pairs for polymorphic detection |

Default polymorphic patterns:

```json
[
  { "typeColumn": "owner_type", "idColumn": "owner_id" },
  { "typeColumn": "account_type", "idColumn": "account_id" }
]
```

### Overrides

Per-table or per-category feature and validation overrides. Keys can be:

- **Exact table name** — `"audit_log"`
- **Category prefix** — `"category:reference"`
- **Glob pattern** — `"staging_*"`

Only non-null properties take effect; everything else inherits from the global config. Both `features` and `validation` blocks are supported, with the same property names as the global sections but as nullable booleans.

## SQL Annotations

Annotate table files with SQL comments to set category and description:

```sql
-- @category core
-- @description User accounts table

CREATE TABLE [dbo].[users]
(
    [id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    [username] VARCHAR(100) NOT NULL,
    [active] BIT NOT NULL DEFAULT 1,
    [created_by] UNIQUEIDENTIFIER NOT NULL,
    [updated_by] UNIQUEIDENTIFIER NOT NULL,
    [valid_from] DATETIME2(7) GENERATED ALWAYS AS ROW START NOT NULL,
    [valid_to] DATETIME2(7) GENERATED ALWAYS AS ROW END NOT NULL,
    PERIOD FOR SYSTEM_TIME ([valid_from], [valid_to])
)
WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = [dbo].[users_history]));
GO
```

## Pattern Detection

### Soft Delete

Detected when a table has both temporal versioning (`SYSTEM_VERSIONING = ON`) and the configured active column (default: `active`). A hard-delete trigger is generated automatically, using the table's actual primary key:

```sql
CREATE TRIGGER [dbo].[trg_users_hard_delete]
ON [dbo].[users]
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;

    IF UPDATE(active)
    BEGIN
        DELETE FROM [dbo].[users]
        WHERE id IN (
            SELECT i.id
            FROM inserted i
            JOIN deleted d ON i.id = d.id
            WHERE i.active = 0 AND d.active = 1
        );
    END
END;
GO
```

Setting `active = 0` causes the row to be hard-deleted; its prior state is preserved in the temporal history table. To query deleted records:

```sql
SELECT * FROM users FOR SYSTEM_TIME ALL
WHERE id = @id AND active = 0
  AND NOT EXISTS (SELECT 1 FROM users WHERE id = @id)
```

### Polymorphic

Detected when a table has a type discriminator column and a corresponding ID column matching one of the configured `polymorphicPatterns` (default: `owner_type`/`owner_id` and `account_type`/`account_id`), plus a CHECK constraint on the type column:

```sql
CREATE TABLE [dbo].[addresses]
(
    [id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    [owner_type] VARCHAR(20) NOT NULL
        CONSTRAINT [ck_addresses_owner_type]
        CHECK ([owner_type] IN ('user', 'company')),
    [owner_id] UNIQUEIDENTIFIER NOT NULL,
    [street] VARCHAR(200) NOT NULL
);
```

### Append-Only

Detected when a non-temporal table has the configured `createdAt` column (default: `created_at`) but no `updatedBy` column (default: `updated_by`) — indicating immutable audit-style records.

## MSBuild Properties

### Default Paths

| Property | Default |
| --- | --- |
| `SchemaToolsConfig` | `$(MSBuildProjectDirectory)\schema-tools.json` |
| `SchemaToolsTablesDirectory` | `$(MSBuildProjectDirectory)\Schema\Tables` |
| `SchemaToolsMetadataOutput` | `$(MSBuildProjectDirectory)\Build\schema-metadata.json` |
| `SchemaToolsDocsOutput` | `$(MSBuildProjectDirectory)\Docs\SCHEMA.md` |
| `SchemaToolsTriggersOutput` | `$(MSBuildProjectDirectory)\Schema\Triggers\_generated` |

### Explicit File List

Instead of scanning a directory, list individual SQL files via the `SchemaToolsTableFile` item group. When present, `SchemaToolsTablesDirectory` is ignored:

```xml
<ItemGroup>
  <SchemaToolsTableFile Include="Schema\Core\*.sql" />
  <SchemaToolsTableFile Include="Schema\Audit\*.sql" />
  <SchemaToolsTableFile Include="Schema\Reference\lookup_table.sql" />
</ItemGroup>
```

### Overrides

```xml
<PropertyGroup>
  <SchemaToolsMetadataOutput>$(MSBuildProjectDirectory)\custom\metadata.json</SchemaToolsMetadataOutput>
  <SchemaToolsDocsOutput>$(MSBuildProjectDirectory)\custom\docs.md</SchemaToolsDocsOutput>
  <SchemaToolsTriggersOutput>$(MSBuildProjectDirectory)\custom\triggers</SchemaToolsTriggersOutput>
</PropertyGroup>
```

### Feature Flags

Disable individual pipeline stages:

```xml
<PropertyGroup>
  <SchemaToolsEnabled>false</SchemaToolsEnabled>              <!-- all stages -->
  <SchemaToolsGenerateMetadata>false</SchemaToolsGenerateMetadata>
  <SchemaToolsValidate>false</SchemaToolsValidate>
  <SchemaToolsGenerateTriggers>false</SchemaToolsGenerateTriggers>
  <SchemaToolsGenerateDocs>false</SchemaToolsGenerateDocs>
</PropertyGroup>
```

Validation settings are controlled exclusively through `schema-tools.json`.

## Custom Triggers

Place hand-written triggers in `Schema\Triggers\_custom\` to suppress the corresponding auto-generated trigger:

```
Schema/
├── Tables/
│   └── users.sql
└── Triggers/
    ├── _generated/     ← auto-generated (safe to commit)
    │   └── trg_users_hard_delete.sql
    └── _custom/        ← hand-written overrides
        └── trg_users_audit.sql
```

## Generated Metadata

`Build/schema-metadata.json`:

```json
{
  "$schema": "./schema-metadata.schema.json",
  "version": "1.0.0",
  "generatedAt": "...",
  "generatedBy": "SchemaMetadataGenerator MSBuild Task",
  "database": "MyDatabase",
  "defaultSchema": "dbo",
  "sqlServerVersion": "Sql160",
  "tables": [
    {
      "name": "users",
      "schema": "dbo",
      "category": "core",
      "description": "User accounts table",
      "hasTemporalVersioning": true,
      "hasActiveColumn": true,
      "hasSoftDelete": true,
      "isAppendOnly": false,
      "isPolymorphic": false,
      "primaryKey": "id",
      "primaryKeyType": "UNIQUEIDENTIFIER",
      "historyTable": "[dbo].[users_history]",
      "columns": [ "..." ],
      "constraints": { "..." : "..." },
      "indexes": [],
      "triggers": {
        "hardDelete": { "generate": true, "name": "trg_users_hard_delete", "activeColumnName": "active" }
      }
    }
  ],
  "statistics": {
    "totalTables": 1,
    "temporalTables": 1,
    "softDeleteTables": 1,
    "appendOnlyTables": 0,
    "polymorphicTables": 0,
    "triggersToGenerate": 1,
    "totalColumns": 7,
    "totalConstraints": 1
  },
  "categories": {}
}
```

## Supported SQL Server Versions

| Version | `sqlServerVersion` |
| --- | --- |
| SQL Server 2008 | `Sql100` |
| SQL Server 2012 | `Sql110` |
| SQL Server 2014 | `Sql120` |
| SQL Server 2016 | `Sql130` |
| SQL Server 2017 | `Sql140` |
| SQL Server 2019 | `Sql150` |
| SQL Server 2022 | `Sql160` (default) |

## Roadmap

- **Warning suppression** — `@suppress` SQL annotations to silence specific validation warnings per table
- **Incremental generation** — skip unchanged `.sql` files on rebuild to improve build times on large schemas
- **JSON Schema generation** — emit a `schema-metadata.schema.json` alongside the metadata file (the `$schema` reference is already present in the output)

## Licence

MIT

## Contributing

Issues and pull requests welcome at: https://github.com/arashi01/schema-tools
